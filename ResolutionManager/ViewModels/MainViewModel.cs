using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ResolutionManager.Helpers;
using ResolutionManager.Models;
using ResolutionManager.Native;
using ResolutionManager.Services;

namespace ResolutionManager.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    // ─── Infrastructure ───────────────────────────────────────────────────────
    private readonly IDisplayService        _displaySvc;
    private readonly IProcessMonitorService _monitorSvc;
    private readonly IConfigurationService  _configSvc;
    private AppConfiguration _config;

    // ─── Observable state ─────────────────────────────────────────────────────
    private ProfileViewModel? _selectedProfile;
    private DisplayResolution? _selectedResolution;
    private DisplayResolution? _currentResolution;
    private DisplayMonitor? _selectedMonitorForProfile;
    private string _statusMessage = "Bem-vindo ao ResSync";
    private bool _isMonitoring;
    private bool _isApplied;
    private int _selectedTabIndex;

    // Settings tab
    private bool _startWithWindows;
    private bool _minimizeToTray;
    private bool _startMinimized;
    private bool _monitorExeAlwaysEnabled;

    // ─── Constructor ──────────────────────────────────────────────────────────
    public MainViewModel(
        IDisplayService displayService,
        IProcessMonitorService processMonitorService,
        IConfigurationService configurationService)
    {
        _displaySvc = displayService;
        _monitorSvc = processMonitorService;
        _configSvc  = configurationService;

        _config = _configSvc.Load();

        // Load settings
        _startWithWindows        = _config.StartWithWindows;
        _minimizeToTray          = _config.MinimizeToTray;
        _startMinimized          = _config.StartMinimized;
        _monitorExeAlwaysEnabled = _config.MonitorExeAlwaysEnabled;

        // Populate monitors
        RefreshMonitors();

        // Populate profiles
        foreach (var p in _config.Profiles)
            Profiles.Add(new ProfileViewModel(p));

        // Load available resolutions for primary monitor initially
        RefreshResolutions(null);

        // Wire process events
        _monitorSvc.ProcessStarted += HandleProcessStarted;
        _monitorSvc.ProcessStopped += HandleProcessStopped;

        // Commands
        AddProfileCommand    = new RelayCommand(AddProfile);
        RemoveProfileCommand = new RelayCommand(RemoveProfile, () => SelectedProfile is not null);
        BrowseExeCommand     = new RelayCommand(BrowseExe,    () => SelectedProfile is not null);
        SaveCommand          = new RelayCommand(Save);
        ApplyNowCommand      = new RelayCommand(ApplyNow,
            () => _isApplied || SelectedProfile?.TargetResolution is not null);
        ToggleMonitorCommand = new RelayCommand(ToggleMonitor);
        SaveSettingsCommand  = new RelayCommand(SaveSettings);
        FixNvidiaCommand     = new RelayCommand(FixNvidia);

        // Apply startup behaviour
        ApplyStartWithWindows(_startWithWindows);

        // Auto-start if persisted
        if (_config.MonitoringEnabled || _config.MonitorExeAlwaysEnabled)
            StartMonitor();
    }

    // ─── Collections ──────────────────────────────────────────────────────────
    public ObservableCollection<ProfileViewModel>  Profiles            { get; } = [];
    public ObservableCollection<DisplayResolution> AvailableResolutions { get; } = [];
    public ObservableCollection<DisplayMonitor>    Monitors             { get; } = [];

    // ─── Bindable Properties ──────────────────────────────────────────────────

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }

    public ProfileViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;

            // Reset applied state when profile selection changes
            IsApplied = false;
            OnPropertyChanged(nameof(ShowVibranceWarning));

            // Sync monitor picker
            SelectedMonitorForProfile = value?.TargetMonitorDeviceName is not null
                ? Monitors.FirstOrDefault(m => m.DeviceName.Equals(
                      value.TargetMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                : Monitors.FirstOrDefault(m => m.IsPrimary);

            // Refresh resolutions for the profile's chosen monitor
            RefreshResolutions(value?.TargetMonitorDeviceName);

            SelectedResolution = value?.TargetResolution is not null
                ? AvailableResolutions.FirstOrDefault(r => r.Equals(value.TargetResolution))
                : null;

            RelayCommand.Refresh();
        }
    }

    public DisplayMonitor? SelectedMonitorForProfile
    {
        get => _selectedMonitorForProfile;
        set
        {
            if (!Set(ref _selectedMonitorForProfile, value)) return;
            if (SelectedProfile is not null)
                SelectedProfile.TargetMonitorDeviceName = value?.DeviceName;

            // Refresh available resolutions for newly selected monitor
            RefreshResolutions(value?.DeviceName);
            SelectedResolution = null;
        }
    }

    public DisplayResolution? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (!Set(ref _selectedResolution, value)) return;
            if (SelectedProfile is not null)
                SelectedProfile.TargetResolution = value;
        }
    }

    public DisplayResolution? CurrentResolution
    {
        get => _currentResolution;
        private set { Set(ref _currentResolution, value); OnPropertyChanged(nameof(CurrentResolutionText)); }
    }

    public string CurrentResolutionText =>
        CurrentResolution is not null
            ? $"Resolução atual:  {CurrentResolution}"
            : "Resolução atual: desconhecida";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set { Set(ref _isMonitoring, value); RelayCommand.Refresh(); }
    }

    /// <summary>True after "Aplicar Agora" is clicked; reverts on next click.</summary>
    public bool IsApplied
    {
        get => _isApplied;
        private set
        {
            Set(ref _isApplied, value);
            OnPropertyChanged(nameof(IsProfileEditable));
            OnPropertyChanged(nameof(ShowVibranceWarning));
            RelayCommand.Refresh();
        }
    }

    /// <summary>Controls whether the profile config fields are interactive.</summary>
    public bool IsProfileEditable => SelectedProfile is not null && !_isApplied;

    // ─── Settings properties ──────────────────────────────────────────────────

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => Set(ref _startWithWindows, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => Set(ref _minimizeToTray, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => Set(ref _startMinimized, value);
    }

    public bool MonitorExeAlwaysEnabled
    {
        get => _monitorExeAlwaysEnabled;
        set => Set(ref _monitorExeAlwaysEnabled, value);
    }

    // ─── Commands ─────────────────────────────────────────────────────────────
    public ICommand AddProfileCommand    { get; }
    public ICommand RemoveProfileCommand { get; }
    public ICommand BrowseExeCommand     { get; }
    public ICommand SaveCommand          { get; }
    public ICommand ApplyNowCommand      { get; }
    public ICommand ToggleMonitorCommand { get; }
    public ICommand SaveSettingsCommand  { get; }
    public ICommand FixNvidiaCommand     { get; }

    /// <summary>True when running on a system with NVIDIA NvAPI support.</summary>
    public bool IsNvidiaAvailable => NvApiService.IsAvailable;

    /// <summary>Human-readable NVIDIA status for display in the UI.</summary>
    public string NvApiStatus => NvApiService.IsAvailable
        ? "Digital Vibrance disponível"
        : $"Driver NVIDIA não detectado: {NvApiService.LastError}";

    /// <summary>Shown next to the vibrance toggle when NvAPI is not available.</summary>
    public bool ShowVibranceWarning => SelectedProfile?.VibranceEnabled == true && !NvApiService.IsAvailable;

    // ─── Command Handlers ─────────────────────────────────────────────────────

    private void AddProfile()
    {
        var model = new AppProfile { Name = "Novo Perfil" };
        var vm = new ProfileViewModel(model);
        Profiles.Add(vm);
        SelectedProfile = vm;
    }

    private void RemoveProfile()
    {
        if (SelectedProfile is null) return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        PersistConfig();
    }

    private void BrowseExe()
    {
        if (SelectedProfile is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Executável (*.exe)|*.exe",
            Title  = "Selecionar o Executável do Jogo"
        };
        if (dlg.ShowDialog() != true) return;
        SelectedProfile.ExecutablePath = dlg.FileName;
        if (string.IsNullOrWhiteSpace(SelectedProfile.Name) || SelectedProfile.Name == "Novo Perfil")
            SelectedProfile.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    private void Save()
    {
        PersistConfig();
        StatusMessage = "Configurações salvas!";
    }

    private void ApplyNow()
    {
        if (_isApplied)
        {
            // Revert
            string? dev = SelectedProfile?.TargetMonitorDeviceName;
            _displaySvc.RestoreResolution(dev);
            _displaySvc.RestoreVibrance(dev);
            _displaySvc.RestoreExtraSaturation(dev);
            CurrentResolution = _displaySvc.GetCurrentResolution(dev);
            StatusMessage = "Configurações revertidas.";
            IsApplied = false;
        }
        else
        {
            // Apply
            if (SelectedProfile?.TargetResolution is null) return;
            string? dev = SelectedProfile.TargetMonitorDeviceName;
            _displaySvc.SetResolution(dev, SelectedProfile.TargetResolution);
            if (SelectedProfile.VibranceEnabled)
            {
                bool vibranceOk = _displaySvc.SetVibrance(dev, SelectedProfile.DigitalVibrance);
                // Write diagnostic log so issues can be pinpointed
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResSync");
                System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(logDir, "nvapi.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | IsAvailable={NvApiService.IsAvailable} | ok={vibranceOk} | {NvApiService.LastError}\n");

                string vibranceInfo = vibranceOk
                    ? $" | Vibrance: {SelectedProfile.DigitalVibrance}% \u2714"
                    : $" | Vibrance n\u00e3o aplicado: {NvApiService.LastError}";
                if (SelectedProfile.ExtraSaturationEnabled)
                    _displaySvc.SetExtraSaturation(dev, SelectedProfile.ExtraSaturation);
                CurrentResolution = _displaySvc.GetCurrentResolution(dev);
                StatusMessage = $"Aplicado: {CurrentResolution}{vibranceInfo}";
            }
            else
            {
                if (SelectedProfile.ExtraSaturationEnabled)
                    _displaySvc.SetExtraSaturation(dev, SelectedProfile.ExtraSaturation);
                CurrentResolution = _displaySvc.GetCurrentResolution(dev);
                StatusMessage = $"Aplicado: {CurrentResolution}";
            }
            IsApplied = true;
        }
    }

    private void ToggleMonitor()
    {
        if (IsMonitoring) StopMonitor();
        else              StartMonitor();
    }

    private void SaveSettings()
    {
        _config.StartWithWindows        = _startWithWindows;
        _config.MinimizeToTray          = _minimizeToTray;
        _config.StartMinimized          = _startMinimized;
        _config.MonitorExeAlwaysEnabled = _monitorExeAlwaysEnabled;
        ApplyStartWithWindows(_startWithWindows);
        PersistConfig();
        StatusMessage = "Configurações gerais salvas!";
    }

    private void FixNvidia()
    {
        var result = MessageBox.Show(
            "Isso vai reiniciar o subsistema gráfico da NVIDIA (Win + Ctrl + Shift + B).\n\n" +
            "• A tela vai piscar brevemente\n" +
            "• Programas em tela cheia podem minimizar\n" +
            "• Configurações de vibrance aplicadas serão perdidas\n\n" +
            "Útil quando o Digital Vibrance não é aplicado ou o driver trava.\n\n" +
            "Deseja continuar?",
            "Fix NVIDIA — Reiniciar Driver",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        // Win + Ctrl + Shift + B is the built-in Windows 8+ GPU driver reset shortcut.
        // It calls DxgkDdiResetFromTimeout internally — safe, no admin rights needed.
        NativeMethods.keybd_event(NativeMethods.VK_LWIN,    0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_SHIFT,   0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_B,       0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_B,       0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_SHIFT,   0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_LWIN,    0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        StatusMessage = "Driver NVIDIA reiniciado via atalho do sistema.";
    }

    // ─── Monitor Lifecycle ────────────────────────────────────────────────────

    private void StartMonitor()
    {
        var active = Profiles
            .Select(p => p.GetModel())
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ExecutableName))
            .ToList();

        if (active.Count == 0)
        {
            StatusMessage = "Adicione ao menos um perfil com executável para monitorar.";
            return;
        }

        _monitorSvc.StartMonitoring(active);
        IsMonitoring  = true;
        StatusMessage = $"Monitorando {active.Count} aplicativo(s)…";
        PersistConfig(monitoringEnabled: true);
    }

    private void StopMonitor()
    {
        _monitorSvc.StopMonitoring();
        IsMonitoring  = false;
        StatusMessage = "Monitoramento pausado.";
        PersistConfig(monitoringEnabled: false);
    }

    // ─── Process Event Handlers ───────────────────────────────────────────────

    private void HandleProcessStarted(object? sender, AppProfile profile)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (profile.TargetResolution is null) return;
            string? dev = profile.TargetMonitorDeviceName;
            _displaySvc.SetResolution(dev, profile.TargetResolution);
            if (profile.DigitalVibrance >= 0)
                _displaySvc.SetVibrance(dev, profile.DigitalVibrance);
            if (profile.ExtraSaturation >= 0)
                _displaySvc.SetExtraSaturation(dev, profile.ExtraSaturation);
            CurrentResolution = _displaySvc.GetCurrentResolution(dev);
            StatusMessage = $"[{profile.Name}] aberto → {profile.TargetResolution}";
        });
    }

    private void HandleProcessStopped(object? sender, AppProfile profile)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            string? dev = profile.TargetMonitorDeviceName;
            _displaySvc.RestoreResolution(dev);
            _displaySvc.RestoreVibrance(dev);
            _displaySvc.RestoreExtraSaturation(dev);
            CurrentResolution = _displaySvc.GetCurrentResolution(dev);
            StatusMessage = $"[{profile.Name}] fechado → resolução restaurada";
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void RefreshMonitors()
    {
        Monitors.Clear();
        foreach (var m in _displaySvc.GetMonitors())
            Monitors.Add(m);
    }

    private void RefreshResolutions(string? deviceName)
    {
        AvailableResolutions.Clear();
        foreach (var r in _displaySvc.GetAvailableResolutions(deviceName))
            AvailableResolutions.Add(r);
        CurrentResolution = _displaySvc.GetCurrentResolution(deviceName);
    }

    private void PersistConfig(bool? monitoringEnabled = null)
    {
        _config.Profiles              = [.. Profiles.Select(p => p.GetModel())];
        _config.MonitoringEnabled     = monitoringEnabled ?? IsMonitoring;
        _config.StartWithWindows        = _startWithWindows;
        _config.MinimizeToTray          = _minimizeToTray;
        _config.StartMinimized          = _startMinimized;
        _config.MonitorExeAlwaysEnabled = _monitorExeAlwaysEnabled;
        _configSvc.Save(_config);
    }

    private static void ApplyStartWithWindows(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "ResSync";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        if (key is null) return;
        if (enable)
        {
            string? exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is not null) key.SetValue(valueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

