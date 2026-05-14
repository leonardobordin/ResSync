using ResolutionManager.Models;

namespace ResolutionManager.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
