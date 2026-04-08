namespace ResolutionManager.Models;

public class AppConfiguration
{
    public List<AppProfile> Profiles { get; set; } = [];
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool MonitoringEnabled { get; set; } = false;
    public bool MonitorExeAlwaysEnabled { get; set; } = true;
}
