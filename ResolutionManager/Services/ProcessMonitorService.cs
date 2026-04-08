using System.Diagnostics;
using System.IO;
using ResolutionManager.Models;

namespace ResolutionManager.Services;

/// <summary>
/// Polls at a configurable interval (default 1 s) to detect when monitored processes
/// start or stop. Intentionally avoids WMI and kernel callbacks to keep CPU usage
/// negligible — a single Process.GetProcessesByName call every second costs &lt;0.1 ms.
/// </summary>
public sealed class ProcessMonitorService : IProcessMonitorService
{
    /// <summary>Poll interval in milliseconds. 1 000 ms balances responsiveness and CPU load.</summary>
    private const int PollIntervalMs = 1000;

    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private List<AppProfile> _profiles = [];

    // Key = processName (without extension), lowercase
    private readonly Dictionary<string, bool> _running = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<AppProfile>? ProcessStarted;
    public event EventHandler<AppProfile>? ProcessStopped;

    public bool IsMonitoring { get; private set; }

    public void StartMonitoring(IEnumerable<AppProfile> profiles)
    {
        lock (_lock)
        {
            _profiles = profiles
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ExecutableName))
                .ToList();

            _running.Clear();

            IsMonitoring = true;
            // Use Change on existing timer to avoid creating multiple timers
            if (_timer is null)
                _timer = new System.Threading.Timer(Poll, null, 0, PollIntervalMs);
            else
                _timer.Change(0, PollIntervalMs);
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            IsMonitoring = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose() => StopMonitoring();

    // ─────────────────────────────────────────────────────────────────
    // Core polling logic
    // ─────────────────────────────────────────────────────────────────

    private void Poll(object? _)
    {
        List<AppProfile> snapshot;
        lock (_lock)
        {
            if (!IsMonitoring) return;
            snapshot = [.. _profiles];
        }

        foreach (var profile in snapshot)
        {
            string processName = Path.GetFileNameWithoutExtension(profile.ExecutableName);
            bool nowRunning = IsRunning(processName);
            _running.TryGetValue(processName, out bool wasRunning);

            if (nowRunning && !wasRunning)
            {
                _running[processName] = true;
                ProcessStarted?.Invoke(this, profile);
            }
            else if (!nowRunning && wasRunning)
            {
                _running[processName] = false;
                ProcessStopped?.Invoke(this, profile);
            }
        }
    }

    private static bool IsRunning(string processName)
        => Process.GetProcessesByName(processName).Length > 0;
}

