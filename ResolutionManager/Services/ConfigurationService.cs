using System.IO;
using System.Text.Json;
using ResolutionManager.Models;

namespace ResolutionManager.Services;

/// <summary>
/// Persists the application configuration as JSON in %AppData%\ResSync\config.json.
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigFilePath { get; }

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "ResSync");
        Directory.CreateDirectory(folder);
        ConfigFilePath = Path.Combine(folder, "config.json");
    }

    public AppConfiguration Load()
    {
        if (!File.Exists(ConfigFilePath))
            return new AppConfiguration();

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions)
                   ?? new AppConfiguration();
        }
        catch
        {
            // Return defaults if the file is corrupt
            return new AppConfiguration();
        }
    }

    public void Save(AppConfiguration configuration)
    {
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }
}
