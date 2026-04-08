using System.Runtime.InteropServices;
using ResolutionManager.Models;
using ResolutionManager.Native;

namespace ResolutionManager.Services;

/// <summary>
/// Manages display resolution via Win32 P/Invoke (EnumDisplaySettings / ChangeDisplaySettings).
/// Stores the original resolution so it can be restored after a game closes.
/// </summary>
public sealed class ResolutionService : IResolutionService
{
    private DisplayResolution? _originalResolution;

    public bool HasSavedOriginal => _originalResolution is not null;

    // ─────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<DisplayResolution> GetAvailableResolutions()
    {
        var seen = new HashSet<(int, int, int)>();
        var list = new List<DisplayResolution>();

        var dm = NewDevMode();
        int mode = 0;

        while (NativeMethods.EnumDisplaySettings(null, mode++, ref dm))
        {
            if (dm.dmBitsPerPel < 16)
                continue;

            var key = (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
            if (!seen.Add(key))
                continue;

            list.Add(new DisplayResolution
            {
                Width = dm.dmPelsWidth,
                Height = dm.dmPelsHeight,
                RefreshRate = dm.dmDisplayFrequency,
                BitsPerPixel = dm.dmBitsPerPel
            });
        }

        return list
            .OrderByDescending(r => r.Width)
            .ThenByDescending(r => r.Height)
            .ThenByDescending(r => r.RefreshRate)
            .ToList();
    }

    public DisplayResolution GetCurrentResolution()
    {
        var dm = NewDevMode();
        NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
        return new DisplayResolution
        {
            Width = dm.dmPelsWidth,
            Height = dm.dmPelsHeight,
            RefreshRate = dm.dmDisplayFrequency,
            BitsPerPixel = dm.dmBitsPerPel
        };
    }

    public bool SetResolution(DisplayResolution resolution)
    {
        // Persist original only once per "session" (first call after restore)
        if (_originalResolution is null)
            _originalResolution = GetCurrentResolution();

        return Apply(resolution);
    }

    public bool RestoreOriginalResolution()
    {
        if (_originalResolution is null)
            return false;

        bool ok = Apply(_originalResolution);
        if (ok)
            _originalResolution = null;
        return ok;
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static bool Apply(DisplayResolution res)
    {
        var dm = NewDevMode();
        dm.dmPelsWidth = res.Width;
        dm.dmPelsHeight = res.Height;
        dm.dmDisplayFrequency = res.RefreshRate;
        dm.dmBitsPerPel = (short)res.BitsPerPixel;
        dm.dmFields = NativeMethods.DM_PELSWIDTH
                    | NativeMethods.DM_PELSHEIGHT
                    | NativeMethods.DM_DISPLAYFREQUENCY
                    | NativeMethods.DM_BITSPERPEL;

        return NativeMethods.ChangeDisplaySettingsEx(
                   null, ref dm, IntPtr.Zero,
                   NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero)
               == NativeMethods.DISP_CHANGE_SUCCESSFUL;
    }

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return dm;
    }
}
