namespace ResolutionManager.Models;

public class AppConfiguration
{
    public List<AppProfile> Profiles { get; set; } = [];
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool MonitoringEnabled { get; set; } = false;
    public bool MonitorExeAlwaysEnabled { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public AppliedDisplayState AppliedState { get; set; } = new();
}

public class AppliedDisplayState
{
    public string? ProfileId { get; set; }
    public string? TargetMonitorDeviceName { get; set; }
    public string? TargetMonitorStableId { get; set; }
    public DisplayResolution? OriginalResolution { get; set; }
    public int? OriginalVibranceRawLevel { get; set; }
    public bool ResolutionApplied { get; set; }
    public bool SaturationApplied { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
