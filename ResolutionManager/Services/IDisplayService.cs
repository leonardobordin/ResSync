using ResolutionManager.Models;

namespace ResolutionManager.Services;

public interface IDisplayService
{
    /// <summary>Returns all monitors currently connected to the desktop.</summary>
    IReadOnlyList<DisplayMonitor> GetMonitors();

    /// <summary>Returns the available resolutions for the given device name (null = primary).</summary>
    IReadOnlyList<DisplayResolution> GetAvailableResolutions(string? deviceName);

    /// <summary>Returns the current resolution for the given device name (null = primary).</summary>
    DisplayResolution GetCurrentResolution(string? deviceName);

    /// <summary>Changes the resolution on the specified monitor. Returns true on success.</summary>
    bool SetResolution(string? deviceName, DisplayResolution resolution);

    /// <summary>Restores the resolution active before SetResolution, or applies the supplied exit resolution.</summary>
    bool RestoreResolution(string? deviceName, DisplayResolution? exitResolution = null);

    /// <summary>Sets NVIDIA Digital Vibrance for a monitor. percent: 0 = neutral, 100 = driver maximum.</summary>
    bool SetVibrance(string? deviceName, int percent);

    /// <summary>Returns current NVIDIA Digital Vibrance percent, or null when unavailable.</summary>
    int? GetCurrentVibrance(string? deviceName);

    /// <summary>Returns current raw NVIDIA Digital Vibrance driver level, or null when unavailable.</summary>
    int? GetCurrentVibranceRawLevel(string? deviceName);

    /// <summary>Restores the NVIDIA Digital Vibrance level that was active before the last SetVibrance call.</summary>
    bool RestoreVibrance(string? deviceName);

    /// <summary>Restores a previously captured raw NVIDIA Digital Vibrance driver level.</summary>
    bool RestoreVibranceLevel(string? deviceName, int rawLevel);

    /// <summary>Applies the experimental gamma-ramp boost used only beyond the NVIDIA driver limit.</summary>
    bool SetExtraSaturation(string? deviceName, int percent);

    /// <summary>Restores the gamma ramp saved before the last SetExtraSaturation call.</summary>
    bool RestoreExtraSaturation(string? deviceName);

    /// <summary>Clears a stale extra-saturation gamma ramp by applying a neutral linear ramp.</summary>
    bool ResetExtraSaturation(string? deviceName);

    /// <summary>Returns true when the current gamma ramp differs from a neutral linear ramp.</summary>
    bool IsExtraSaturationActive(string? deviceName);
}
