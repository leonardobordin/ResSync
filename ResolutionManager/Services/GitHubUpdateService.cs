using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ResolutionManager.Models;

namespace ResolutionManager.Services;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/leonardobordin/ResSync/releases/latest";
    private static readonly HttpClient Http = CreateHttpClient();

    public string CurrentVersion { get; } = ResolveCurrentVersion();

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        string tagName = root.TryGetProperty("tag_name", out JsonElement tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;
        string releaseUrl = root.TryGetProperty("html_url", out JsonElement htmlElement)
            ? htmlElement.GetString() ?? "https://github.com/leonardobordin/ResSync/releases/latest"
            : "https://github.com/leonardobordin/ResSync/releases/latest";

        string? downloadUrl = ResolveDownloadUrl(root);
        string latestVersion = NormalizeVersionText(tagName);
        bool isUpdateAvailable = CompareVersions(latestVersion, CurrentVersion) > 0;

        return new UpdateInfo(
            CurrentVersion,
            latestVersion,
            tagName,
            releaseUrl,
            downloadUrl,
            isUpdateAvailable);
    }

    public static bool OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not open update URL: {ex.Message}");
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ResSync-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static string ResolveCurrentVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return NormalizeVersionText(informationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    private static string? ResolveDownloadUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            return asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                ? urlElement.GetString()
                : null;
        }

        return null;
    }

    private static int CompareVersions(string left, string right)
    {
        int[] leftParts = ParseVersionParts(left);
        int[] rightParts = ParseVersionParts(right);
        int length = Math.Max(leftParts.Length, rightParts.Length);

        for (int i = 0; i < length; i++)
        {
            int leftValue = i < leftParts.Length ? leftParts[i] : 0;
            int rightValue = i < rightParts.Length ? rightParts[i] : 0;
            int comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private static int[] ParseVersionParts(string version)
        => NormalizeVersionText(version)
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out int value) ? value : 0)
            .ToArray();

    private static string NormalizeVersionText(string version)
    {
        string text = version.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        int metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0)
            text = text[..metadataIndex];

        int suffixIndex = text.IndexOf('-');
        if (suffixIndex >= 0)
            text = text[..suffixIndex];

        return string.IsNullOrWhiteSpace(text) ? "0.0.0" : text;
    }
}
