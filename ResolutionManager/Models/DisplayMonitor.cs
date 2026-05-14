namespace ResolutionManager.Models;

/// <summary>Represents a physical display / monitor attached to the system.</summary>
public class DisplayMonitor
{
    /// <summary>Win32 device name, e.g. "\\.\DISPLAY1".</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Human-readable adapter / monitor description, e.g. "NVIDIA GeForce RTX 4080".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Stable PnP/registry identity for matching the same physical monitor after DISPLAY renumbering.</summary>
    public string StableId { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public override string ToString()
    {
        string description = string.IsNullOrWhiteSpace(Description)
            ? DeviceName
            : Description;

        string label = GetWindowsDisplayLabel();
        string text = string.IsNullOrWhiteSpace(label)
            ? description
            : $"{label}: {description}";

        return IsPrimary ? $"{text}  (principal)" : text;
    }

    private string GetWindowsDisplayLabel()
    {
        const string prefix = @"\\.\DISPLAY";

        if (!DeviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        string number = DeviceName[prefix.Length..];
        return string.IsNullOrWhiteSpace(number) ? string.Empty : $"Tela {number}";
    }
}
