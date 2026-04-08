using ResolutionManager.Models;

namespace ResolutionManager.Services;

/// <summary>Kept for backwards compatibility. New code should use IDisplayService directly.</summary>
public interface IResolutionService
{
    IReadOnlyList<DisplayResolution> GetAvailableResolutions();
    DisplayResolution GetCurrentResolution();
    bool SetResolution(DisplayResolution resolution);
    bool RestoreOriginalResolution();
    bool HasSavedOriginal { get; }
}
