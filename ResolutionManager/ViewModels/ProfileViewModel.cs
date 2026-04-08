using System.IO;
using ResolutionManager.Models;

namespace ResolutionManager.ViewModels;

/// <summary>
/// Wraps an AppProfile for two-way binding in the UI.
/// </summary>
public sealed class ProfileViewModel : BaseViewModel
{
    private readonly AppProfile _model;
    private int _vibranceValue;    // 0–100 for the slider, independent of the -1 sentinel
    private int _saturationValue;  // 0–100 for extra saturation slider

    public ProfileViewModel(AppProfile model)
    {
        _model = model;
        _vibranceValue   = model.DigitalVibrance   >= 0 ? model.DigitalVibrance   : 100;
        _saturationValue = model.ExtraSaturation   >= 0 ? model.ExtraSaturation   : 30;
    }

    public AppProfile GetModel() => _model;

    public string Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); }
    }

    public string ExecutablePath
    {
        get => _model.ExecutablePath;
        set
        {
            _model.ExecutablePath = value;
            _model.ExecutableName = Path.GetFileName(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExecutableName));
        }
    }

    public string ExecutableName => _model.ExecutableName;

    public DisplayResolution? TargetResolution
    {
        get => _model.TargetResolution;
        set { _model.TargetResolution = value; OnPropertyChanged(); OnPropertyChanged(nameof(TargetResolutionDisplay)); }
    }

    public string TargetResolutionDisplay =>
        _model.TargetResolution is not null ? _model.TargetResolution.ToString() : "—";

    /// <summary>Win32 device name of the target monitor. Null means primary.</summary>
    public string? TargetMonitorDeviceName
    {
        get => _model.TargetMonitorDeviceName;
        set { _model.TargetMonitorDeviceName = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Slider / display value: always 0–100.
    /// The underlying model stores -1 when disabled; this property never returns -1.
    /// </summary>
    public int DigitalVibrance
    {
        get => _vibranceValue;
        set
        {
            _vibranceValue = Math.Clamp(value, 0, 100);
            if (_model.DigitalVibrance >= 0)       // only push to model when enabled
                _model.DigitalVibrance = _vibranceValue;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether vibrance override is enabled for this profile.</summary>
    public bool VibranceEnabled
    {
        get => _model.DigitalVibrance >= 0;
        set
        {
            _model.DigitalVibrance = value ? _vibranceValue : -1;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Slider / display value for extra saturation S-curve: always 0–100.
    /// The underlying model stores -1 when disabled.
    /// </summary>
    public int ExtraSaturation
    {
        get => _saturationValue;
        set
        {
            _saturationValue = Math.Clamp(value, 0, 100);
            if (_model.ExtraSaturation >= 0)
                _model.ExtraSaturation = _saturationValue;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether extra saturation S-curve is enabled for this profile.</summary>
    public bool ExtraSaturationEnabled
    {
        get => _model.ExtraSaturation >= 0;
        set
        {
            _model.ExtraSaturation = value ? _saturationValue : -1;
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => _model.IsEnabled;
        set { _model.IsEnabled = value; OnPropertyChanged(); }
    }
}

