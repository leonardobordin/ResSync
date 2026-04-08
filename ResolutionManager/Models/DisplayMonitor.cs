namespace ResolutionManager.Models;

/// <summary>Represents a physical display / monitor attached to the system.</summary>
public class DisplayMonitor
{
    /// <summary>Win32 device name, e.g. "\\.\DISPLAY1".</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Human-readable adapter / monitor description, e.g. "NVIDIA GeForce RTX 4080".</summary>
    public string Description { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public override string ToString() =>
        IsPrimary ? $"{Description}  (principal)" : Description;
}
