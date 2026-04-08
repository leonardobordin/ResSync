namespace ResolutionManager.Models;

public class AppProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ExecutableName { get; set; } = string.Empty;
    public DisplayResolution? TargetResolution { get; set; }

    /// <summary>DeviceName of target monitor, e.g. "\\\\.\\DISPLAY1". Null = primary.</summary>
    public string? TargetMonitorDeviceName { get; set; }

    /// <summary>Digital Vibrance 0–100 (mapped to driver range). -1 = do not change.</summary>
    public int DigitalVibrance { get; set; } = -1;

    /// <summary>Extra Saturation S-curve intensity 0–100. -1 = do not change.</summary>
    public int ExtraSaturation { get; set; } = -1;

    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
