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

    /// <summary>Restores the resolution that was active before the last SetResolution call.</summary>
    bool RestoreResolution(string? deviceName);

    /// <summary>
    /// Sets the digital saturation / vibrance for a monitor using the GDI gamma ramp approach.
    /// <para>percent: 0 = natural, 100 = full driver limit (≈ 2× saturation).</para>
    /// Returns true if the ramp was applied.
    /// </summary>
    bool SetVibrance(string? deviceName, int percent);

    /// <summary>Restores the gamma ramp that was active before the last SetVibrance call.</summary>
    bool RestoreVibrance(string? deviceName);

    /// <summary>
    /// Applies an S-curve gamma ramp on top of the current colour pipeline, making colours more vivid.
    /// <para>percent: 0 = natural, 100 = maximum S-curve intensity.</para>
    /// Returns true if the ramp was applied.
    /// </summary>
    bool SetExtraSaturation(string? deviceName, int percent);

    /// <summary>Restores the gamma ramp saved before the last SetExtraSaturation call.</summary>
    bool RestoreExtraSaturation(string? deviceName);
}
