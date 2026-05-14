namespace ResolutionManager.Models;

public sealed record UpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string TagName,
    string ReleaseUrl,
    string? DownloadUrl,
    bool IsUpdateAvailable);
