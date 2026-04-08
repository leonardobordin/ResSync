using ResolutionManager.Models;

namespace ResolutionManager.Services;

public interface IConfigurationService
{
    AppConfiguration Load();
    void Save(AppConfiguration configuration);
    string ConfigFilePath { get; }
}
