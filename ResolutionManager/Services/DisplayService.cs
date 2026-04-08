using System.Runtime.InteropServices;
using ResolutionManager.Models;
using ResolutionManager.Native;

namespace ResolutionManager.Services;

/// <summary>
/// Manages display resolution and digital vibrance via Win32 P/Invoke.
/// Supports multi-monitor by targeting a specific device name (e.g. "\\.\DISPLAY1").
/// Vibrance is controlled exclusively via NvAPI (NVIDIA Digital Vibrance).
/// </summary>
public sealed class DisplayService : IDisplayService
{
    // ── Per-monitor saved originals ────────────────────────────────────────────
    private readonly Dictionary<string, DisplayResolution> _savedResolutions = new(StringComparer.OrdinalIgnoreCase);
    // Raw NvAPI DVC levels saved before we changed them (for exact restoration)
    private readonly Dictionary<string, int>               _savedNvDvc       = new(StringComparer.OrdinalIgnoreCase);
    // Tracks which monitors have had vibrance applied
    private readonly HashSet<string>                       _nvDvcActive      = new(StringComparer.OrdinalIgnoreCase);
    // GDI gamma ramps saved before extra saturation was applied
    private readonly Dictionary<string, RAMP>               _savedRamps     = new(StringComparer.OrdinalIgnoreCase);
    // Tracks which monitors have an active extra-saturation ramp
    private readonly HashSet<string>                       _satActive        = new(StringComparer.OrdinalIgnoreCase);

    // ── Monitor enumeration ───────────────────────────────────────────────────

    public IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        var device = NewDisplayDevice();
        uint i = 0;
        while (NativeMethods.EnumDisplayDevices(null, i++, ref device, 0))
        {
            if ((device.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                continue;

            // Query the monitor attached to this adapter for a friendly name
            var monitor = NewDisplayDevice();
            string description = device.DeviceString;
            if (NativeMethods.EnumDisplayDevices(device.DeviceName, 0, ref monitor, 0)
                && !string.IsNullOrWhiteSpace(monitor.DeviceString))
            {
                description = monitor.DeviceString;
            }

            monitors.Add(new DisplayMonitor
            {
                DeviceName  = device.DeviceName,
                Description = description,
                IsPrimary   = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0
            });
        }
        return monitors;
    }

    // ── Resolution API ────────────────────────────────────────────────────────

    public IReadOnlyList<DisplayResolution> GetAvailableResolutions(string? deviceName)
    {
        var seen = new HashSet<(int, int, int)>();
        var list = new List<DisplayResolution>();
        var dm = NewDevMode();
        int mode = 0;
        while (NativeMethods.EnumDisplaySettings(deviceName, mode++, ref dm))
        {
            if (dm.dmBitsPerPel < 16) continue;
            var key = (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
            if (!seen.Add(key)) continue;
            list.Add(new DisplayResolution
            {
                Width        = dm.dmPelsWidth,
                Height       = dm.dmPelsHeight,
                RefreshRate  = dm.dmDisplayFrequency,
                BitsPerPixel = dm.dmBitsPerPel
            });
        }
        return list
            .OrderByDescending(r => r.Width)
            .ThenByDescending(r => r.Height)
            .ThenByDescending(r => r.RefreshRate)
            .ToList();
    }

    public DisplayResolution GetCurrentResolution(string? deviceName)
    {
        var dm = NewDevMode();
        NativeMethods.EnumDisplaySettings(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
        return new DisplayResolution
        {
            Width        = dm.dmPelsWidth,
            Height       = dm.dmPelsHeight,
            RefreshRate  = dm.dmDisplayFrequency,
            BitsPerPixel = dm.dmBitsPerPel
        };
    }

    public bool SetResolution(string? deviceName, DisplayResolution resolution)
    {
        string key = NormaliseKey(deviceName);
        if (!_savedResolutions.ContainsKey(key))
            _savedResolutions[key] = GetCurrentResolution(deviceName);

        return ApplyResolution(deviceName, resolution);
    }

    public bool RestoreResolution(string? deviceName)
    {
        string key = NormaliseKey(deviceName);
        if (!_savedResolutions.TryGetValue(key, out var saved)) return false;
        bool ok = ApplyResolution(deviceName, saved);
        if (ok) _savedResolutions.Remove(key);
        return ok;
    }

    // ── Vibrance: NvAPI (NVIDIA Digital Vibrance) ─────────────────────────────

    /// <summary>
    /// Sets digital vibrance on the specified monitor via NvAPI.
    /// This changes the same setting visible in the NVIDIA Control Panel.
    /// percent 0 = neutral (50% in NVIDIA panel), percent 100 = maximum.
    /// </summary>
    public bool SetVibrance(string? deviceName, int percent)
    {
        if (!NvApiService.IsAvailable) return false;

        percent = Math.Clamp(percent, 0, 100);
        string key = NormaliseKey(deviceName);

        // Save the original raw driver level once for later restore
        if (!_savedNvDvc.ContainsKey(key))
        {
            int orig = NvApiService.GetCurrentLevel(deviceName);
            _savedNvDvc[key] = orig != int.MinValue ? orig : 0;
        }

        bool ok = NvApiService.SetVibrance(deviceName, percent);
        if (ok) _nvDvcActive.Add(key);
        return ok;
    }

    public bool RestoreVibrance(string? deviceName)
    {
        string key = NormaliseKey(deviceName);

        if (!_nvDvcActive.Contains(key) || !_savedNvDvc.TryGetValue(key, out int savedRawLevel))
            return false;

        bool ok = NvApiService.RestoreToLevel(deviceName, savedRawLevel);
        if (ok)
        {
            _nvDvcActive.Remove(key);
            _savedNvDvc.Remove(key);
        }
        return ok;
    }

    // ── Extra Saturation via GDI S-curve gamma ramp ───────────────────────────

    /// <summary>
    /// Applies an S-curve gamma ramp that makes colours appear more vivid.
    /// Stackable on top of NvAPI Digital Vibrance; uses GDI SetDeviceGammaRamp.
    /// </summary>
    public bool SetExtraSaturation(string? deviceName, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        string key = NormaliseKey(deviceName);

        IntPtr hDc = CreateMonitorDC(deviceName);
        if (hDc == IntPtr.Zero) return false;

        try
        {
            // Save original ramp only once per monitor per session
            if (!_savedRamps.ContainsKey(key))
            {
                var orig = new RAMP
                {
                    Red   = new ushort[256],
                    Green = new ushort[256],
                    Blue  = new ushort[256]
                };
                if (!NativeMethods.GetDeviceGammaRamp(hDc, ref orig))
                {
                    // Driver doesn't support read-back; build a neutral linear ramp
                    for (int i = 0; i < 256; i++)
                        orig.Red[i] = orig.Green[i] = orig.Blue[i] = (ushort)(i * 257);
                }
                _savedRamps[key] = orig;
            }

            RAMP ramp = BuildSaturationRamp(percent);
            bool ok = NativeMethods.SetDeviceGammaRamp(hDc, ref ramp);
            if (ok) _satActive.Add(key);
            return ok;
        }
        finally
        {
            NativeMethods.DeleteDC(hDc);
        }
    }

    public bool RestoreExtraSaturation(string? deviceName)
    {
        string key = NormaliseKey(deviceName);
        if (!_satActive.Contains(key)) return false;

        IntPtr hDc = CreateMonitorDC(deviceName);
        if (hDc == IntPtr.Zero) return false;

        try
        {
            RAMP ramp;
            if (_savedRamps.TryGetValue(key, out var saved))
            {
                ramp = saved;
            }
            else
            {
                // Fallback: linear (neutral) ramp
                ramp = new RAMP
                {
                    Red   = new ushort[256],
                    Green = new ushort[256],
                    Blue  = new ushort[256]
                };
                for (int i = 0; i < 256; i++)
                    ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = (ushort)(i * 257);
            }

            bool ok = NativeMethods.SetDeviceGammaRamp(hDc, ref ramp);
            if (ok)
            {
                _satActive.Remove(key);
                _savedRamps.Remove(key);
            }
            return ok;
        }
        finally
        {
            NativeMethods.DeleteDC(hDc);
        }
    }

    /// <summary>
    /// Builds an S-curve gamma ramp:  darks → darker, lights → lighter, midpoint preserved.
    /// Formula: f(t) = t + k·(2t-1)·t·(1-t)  where k = percent/100 × 2.0
    /// </summary>
    private static RAMP BuildSaturationRamp(int percent)
    {
        double k = (percent / 100.0) * 2.0;
        var ramp = new RAMP
        {
            Red   = new ushort[256],
            Green = new ushort[256],
            Blue  = new ushort[256]
        };
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            double s = t + k * (2 * t - 1) * t * (1 - t);
            ushort val = (ushort)(Math.Clamp(s, 0.0, 1.0) * 65535);
            ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = val;
        }
        return ramp;
    }

    /// <summary>Creates a GDI device context for the given display device name.</summary>
    private static IntPtr CreateMonitorDC(string? deviceName)
        => NativeMethods.CreateDC("DISPLAY",
               string.IsNullOrWhiteSpace(deviceName) ? null : deviceName,
               null, IntPtr.Zero);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool ApplyResolution(string? deviceName, DisplayResolution res)
    {
        var dm = NewDevMode();
        dm.dmPelsWidth        = res.Width;
        dm.dmPelsHeight       = res.Height;
        dm.dmDisplayFrequency = res.RefreshRate;
        dm.dmBitsPerPel       = (short)res.BitsPerPixel;
        dm.dmFields = NativeMethods.DM_PELSWIDTH
                    | NativeMethods.DM_PELSHEIGHT
                    | NativeMethods.DM_DISPLAYFREQUENCY
                    | NativeMethods.DM_BITSPERPEL;

        return NativeMethods.ChangeDisplaySettingsEx(
                   deviceName, ref dm, IntPtr.Zero,
                   NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero)
               == NativeMethods.DISP_CHANGE_SUCCESSFUL;
    }

    private static string NormaliseKey(string? deviceName)
        => string.IsNullOrWhiteSpace(deviceName) ? "__PRIMARY__" : deviceName.ToUpperInvariant();

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return dm;
    }

    private static DISPLAY_DEVICE NewDisplayDevice()
    {
        var d = new DISPLAY_DEVICE();
        d.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        return d;
    }
}
