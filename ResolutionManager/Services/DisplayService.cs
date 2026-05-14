using System.Runtime.InteropServices;
using System.Management;
using System.Text;
using Microsoft.Win32;
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
        var wmiMonitorNames = GetWmiMonitorNamesByHardwareId();

        for (uint i = 0; ; i++)
        {
            var device = NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, i, ref device, 0))
                break;

            if ((device.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                continue;

            // Query the monitor attached to this adapter for a friendly name and stable identity.
            var monitor = NewDisplayDevice();
            string description = device.DeviceString;
            string stableId = BuildStableMonitorId(device);
            if (NativeMethods.EnumDisplayDevices(device.DeviceName, 0, ref monitor, 0))
            {
                description = ResolveMonitorDescription(monitor, device.DeviceString, wmiMonitorNames);
                stableId = BuildStableMonitorId(monitor) ?? stableId;
            }

            monitors.Add(new DisplayMonitor
            {
                DeviceName  = device.DeviceName,
                Description = description,
                StableId    = stableId,
                IsPrimary   = (device.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0
            });
        }
        return monitors;
    }

    private static string BuildStableMonitorId(DISPLAY_DEVICE displayDevice)
        => NormalizeMonitorIdentity(displayDevice.DeviceID)
           ?? NormalizeMonitorIdentity(displayDevice.DeviceKey)
           ?? string.Empty;

    private static string? NormalizeMonitorIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return null;

        const string machineRegistryPrefix = @"\Registry\Machine\";

        string normalized = identity.Trim();
        if (normalized.StartsWith(machineRegistryPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[machineRegistryPrefix.Length..];

        string[] parts = normalized
            .Replace('#', '\\')
            .Replace('/', '\\')
            .Trim('\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0
            ? null
            : string.Join("\\", parts).ToUpperInvariant();
    }

    private static string ResolveMonitorDescription(
        DISPLAY_DEVICE monitor,
        string fallback,
        IReadOnlyDictionary<string, string> wmiMonitorNames)
    {
        string? wmiName = ResolveWmiMonitorName(monitor, wmiMonitorNames);
        if (IsUsableMonitorName(wmiName))
            return wmiName!;

        string? registryName = ReadMonitorNameFromRegistry(monitor.DeviceID)
            ?? ReadMonitorNameFromRegistry(monitor.DeviceKey);

        if (IsUsableMonitorName(registryName))
            return registryName!;

        if (IsUsableMonitorName(monitor.DeviceString))
            return monitor.DeviceString;

        return string.IsNullOrWhiteSpace(fallback) ? "Monitor" : fallback;
    }

    private static Dictionary<string, string> GetWmiMonitorNamesByHardwareId()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");

            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string? instanceName = item["InstanceName"] as string;
                    string? friendlyName = DecodeWmiString(item["UserFriendlyName"]);
                    if (!IsUsableMonitorName(friendlyName))
                        continue;

                    foreach (string hardwareId in ExtractMonitorHardwareIds(instanceName))
                    {
                        names.TryAdd(hardwareId, friendlyName!);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not resolve monitor names from WMI: {ex.Message}");
        }

        return names;
    }

    private static string? ResolveWmiMonitorName(
        DISPLAY_DEVICE monitor,
        IReadOnlyDictionary<string, string> wmiMonitorNames)
    {
        foreach (string hardwareId in ExtractMonitorHardwareIds(monitor.DeviceID)
                     .Concat(ExtractMonitorHardwareIds(monitor.DeviceKey)))
        {
            if (wmiMonitorNames.TryGetValue(hardwareId, out string? name)
                && IsUsableMonitorName(name))
            {
                return name;
            }
        }

        return null;
    }

    private static string? ReadMonitorNameFromRegistry(string? deviceIdentity)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentity))
            return null;

        try
        {
            foreach (string path in BuildMonitorRegistryPaths(deviceIdentity))
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
                string? name = ReadMonitorNameFromRegistryKey(key, depth: 0);
                if (IsUsableMonitorName(name))
                    return name;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not resolve monitor name from registry: {ex.Message}");
        }

        return null;
    }

    private static IEnumerable<string> BuildMonitorRegistryPaths(string deviceIdentity)
    {
        const string machineRegistryPrefix = @"\Registry\Machine\";

        if (deviceIdentity.StartsWith(machineRegistryPrefix, StringComparison.OrdinalIgnoreCase))
            yield return deviceIdentity[machineRegistryPrefix.Length..].TrimStart('\\');

        string normalized = deviceIdentity
            .Replace('#', '\\')
            .Replace('/', '\\')
            .Trim('\\');

        foreach (string hardwareId in ExtractMonitorHardwareIds(normalized))
        {
            yield return $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hardwareId}";
            yield return $@"SYSTEM\CurrentControlSet\Enum\MONITOR\{hardwareId}";
        }
    }

    private static IEnumerable<string> ExtractMonitorHardwareIds(string? deviceIdentity)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentity))
            yield break;

        string normalized = deviceIdentity
            .Replace('#', '\\')
            .Replace('/', '\\')
            .Trim('\\');

        string[] parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length - 1; index++)
        {
            if (parts[index].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase)
                || parts[index].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
            {
                yield return parts[index + 1];
            }
        }
    }

    private static string? ReadMonitorNameFromRegistryKey(RegistryKey? key, int depth)
    {
        if (key is null || depth > 4)
            return null;

        string? friendlyName = CleanRegistryDisplayName(key.GetValue("FriendlyName") as string);
        if (IsUsableMonitorName(friendlyName))
            return friendlyName;

        using (RegistryKey? parameters = key.OpenSubKey("Device Parameters"))
        {
            string? edidName = ParseEdidMonitorName(parameters?.GetValue("EDID") as byte[]);
            if (IsUsableMonitorName(edidName))
                return edidName;
        }

        foreach (string subKeyName in key.GetSubKeyNames())
        {
            using RegistryKey? subKey = key.OpenSubKey(subKeyName);
            string? childName = ReadMonitorNameFromRegistryKey(subKey, depth + 1);
            if (IsUsableMonitorName(childName))
                return childName;
        }

        return null;
    }

    private static string? ParseEdidMonitorName(byte[]? edid)
    {
        if (edid is null || edid.Length < 128)
            return null;

        string? descriptorText = null;

        for (int offset = 54; offset <= 108 && offset + 18 <= edid.Length; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0)
                continue;

            byte tag = edid[offset + 3];
            if (tag != 0xFC && tag != 0xFE)
                continue;

            string name = Encoding.ASCII
                .GetString(edid, offset + 5, 13)
                .Replace('\0', ' ')
                .Trim();

            if (!IsUsableMonitorName(name))
                continue;

            if (tag == 0xFC)
                return name;

            descriptorText ??= name;
        }

        return descriptorText;
    }

    private static string? CleanRegistryDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string name = value.Trim();
        int separator = name.LastIndexOf(';');
        if (separator >= 0 && separator < name.Length - 1)
            name = name[(separator + 1)..].Trim();

        return name;
    }

    private static string? DecodeWmiString(object? value)
    {
        if (value is null)
            return null;

        var chars = new List<char>();

        if (value is Array values)
        {
            foreach (object? item in values)
            {
                if (item is null)
                    continue;

                ushort code = Convert.ToUInt16(item);
                if (code == 0)
                    break;

                chars.Add((char)code);
            }
        }

        string text = new(chars.ToArray());
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool IsUsableMonitorName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return !name.StartsWith('@')
            && !name.Contains("Generic", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("Genérico", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("Monitor PnP", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("PnP Monitor", StringComparison.OrdinalIgnoreCase);
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

    public bool RestoreResolution(string? deviceName, DisplayResolution? exitResolution = null)
    {
        string key = NormaliseKey(deviceName);
        if (exitResolution is null && !_savedResolutions.TryGetValue(key, out _))
            return false;

        var target = exitResolution ?? _savedResolutions[key];
        bool ok = ApplyResolution(deviceName, target);
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

    public int? GetCurrentVibrance(string? deviceName)
    {
        if (!NvApiService.IsAvailable) return null;

        int percent = NvApiService.GetCurrentPercent(deviceName);
        return percent == int.MinValue ? null : percent;
    }

    public int? GetCurrentVibranceRawLevel(string? deviceName)
    {
        if (!NvApiService.IsAvailable) return null;

        int level = NvApiService.GetCurrentLevel(deviceName);
        return level == int.MinValue ? null : level;
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

    public bool RestoreVibranceLevel(string? deviceName, int rawLevel)
        => NvApiService.IsAvailable && NvApiService.RestoreToLevel(deviceName, rawLevel);

    // ── Extra Saturation via GDI gamma ramp ──────────────────────────────────

    /// <summary>
    /// Applies a mild midpoint-preserving channel curve as an experimental boost beyond
    /// the NVIDIA driver limit. This is not true Digital Vibrance, but avoids the
    /// aggressive lift/dimming that made the image white or too dark.
    /// </summary>
    public bool SetExtraSaturation(string? deviceName, int percent)
    {
        if (!NvApiService.IsAvailable) return false;

        percent = Math.Clamp(percent, 0, 100);
        string key = NormaliseKey(deviceName);

        return WithMonitorDC(deviceName, hDc =>
        {
            if (!_savedRamps.ContainsKey(key))
            {
                var orig = new RAMP
                {
                    Red   = new ushort[256],
                    Green = new ushort[256],
                    Blue  = new ushort[256]
                };

                if (!NativeMethods.GetDeviceGammaRamp(hDc, ref orig))
                    orig = BuildLinearRamp();

                _savedRamps[key] = orig;
            }

            RAMP ramp = BuildExtraSaturationRamp(_savedRamps[key], percent);
            bool ok = NativeMethods.SetDeviceGammaRamp(hDc, ref ramp);
            if (ok) _satActive.Add(key);
            return ok;
        });
    }

    public bool RestoreExtraSaturation(string? deviceName)
    {
        string key = NormaliseKey(deviceName);
        if (!_satActive.Contains(key)) return false;

        return WithMonitorDC(deviceName, hDc =>
        {
            RAMP ramp;
            if (_savedRamps.TryGetValue(key, out var saved))
            {
                ramp = saved;
            }
            else
            {
                // Fallback: linear (neutral) ramp
                ramp = BuildLinearRamp();
            }

            bool ok = NativeMethods.SetDeviceGammaRamp(hDc, ref ramp);
            if (ok)
            {
                _satActive.Remove(key);
                _savedRamps.Remove(key);
            }
            return ok;
        });
    }

    public bool ResetExtraSaturation(string? deviceName)
        => WithMonitorDC(deviceName, hDc =>
        {
            var ramp = BuildLinearRamp();
            return NativeMethods.SetDeviceGammaRamp(hDc, ref ramp);
        });

    public bool IsExtraSaturationActive(string? deviceName)
        => WithMonitorDC(deviceName, hDc =>
        {
            var ramp = new RAMP
            {
                Red   = new ushort[256],
                Green = new ushort[256],
                Blue  = new ushort[256]
            };

            return NativeMethods.GetDeviceGammaRamp(hDc, ref ramp) && !IsLinearRamp(ramp);
        });

    private static RAMP BuildLinearRamp()
    {
        var ramp = new RAMP
        {
            Red   = new ushort[256],
            Green = new ushort[256],
            Blue  = new ushort[256]
        };

        for (int i = 0; i < 256; i++)
            ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = (ushort)(i * 257);

        return ramp;
    }

    private static RAMP BuildExtraSaturationRamp(RAMP baseRamp, int percent)
    {
        // Keep this deliberately conservative. A per-channel gamma ramp can only
        // approximate extra colour punch; high strengths turn into brightness shifts.
        double strength = 0.45 * (percent / 100.0);
        var ramp = new RAMP
        {
            Red   = new ushort[256],
            Green = new ushort[256],
            Blue  = new ushort[256]
        };

        for (int i = 0; i < 256; i++)
        {
            ramp.Red[i]   = ApplyExtraSaturationCurve(baseRamp.Red[i], strength);
            ramp.Green[i] = ApplyExtraSaturationCurve(baseRamp.Green[i], strength);
            ramp.Blue[i]  = ApplyExtraSaturationCurve(baseRamp.Blue[i], strength);
        }

        return ramp;
    }

    private static ushort ApplyExtraSaturationCurve(ushort source, double strength)
    {
        double t = source / 65535.0;
        double curved = t + strength * (2.0 * t - 1.0) * t * (1.0 - t);
        return (ushort)Math.Round(Math.Clamp(curved, 0.0, 1.0) * 65535);
    }

    private static bool IsLinearRamp(RAMP ramp)
    {
        for (int i = 0; i < 256; i++)
        {
            int expected = i * 257;
            if (Math.Abs(ramp.Red[i] - expected) > 768
                || Math.Abs(ramp.Green[i] - expected) > 768
                || Math.Abs(ramp.Blue[i] - expected) > 768)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Creates a GDI device context for the given display device name.</summary>
    private static bool WithMonitorDC(string? deviceName, Func<IntPtr, bool> action)
    {
        foreach (var hDc in CreateMonitorDCs(deviceName))
        {
            try
            {
                if (action(hDc))
                    return true;
            }
            finally
            {
                NativeMethods.DeleteDC(hDc);
            }
        }

        return false;
    }

    private static IEnumerable<IntPtr> CreateMonitorDCs(string? deviceName)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            IntPtr namedDeviceDc = NativeMethods.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            if (namedDeviceDc != IntPtr.Zero)
                yield return namedDeviceDc;

            IntPtr namedDriverDc = NativeMethods.CreateDC(deviceName, null, null, IntPtr.Zero);
            if (namedDriverDc != IntPtr.Zero)
                yield return namedDriverDc;

            yield break;
        }

        IntPtr primaryDc = NativeMethods.CreateDC("DISPLAY", null, null, IntPtr.Zero);
        if (primaryDc != IntPtr.Zero)
            yield return primaryDc;
    }

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
