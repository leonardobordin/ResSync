using ResolutionManager.Models;

namespace ResolutionManager.Services;

public interface IProcessMonitorService : IDisposable
{
    event EventHandler<AppProfile>? ProcessStarted;
    event EventHandler<AppProfile>? ProcessStopped;
    bool IsMonitoring { get; }
    void StartMonitoring(IEnumerable<AppProfile> profiles);
    void StopMonitoring();
}
