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
    private DisplayResolution? _selectedExitResolution;
    private DisplayResolution? _currentResolution;
    private DisplayMonitor? _selectedMonitorForProfile;
    private string _statusMessage = "Bem-vindo ao ResSync";
    private bool _isMonitoring;
    private bool _isApplied;
    private bool _resolutionApplied;
    private bool _saturationApplied;
    private int _selectedTabIndex;

    // Settings tab
    private bool _startWithWindows;
    private bool _minimizeToTray;
    private bool _startMinimized;
    private bool _monitorExeAlwaysEnabled;
    private bool _suppressSelectionPersistence;

    private readonly record struct SaturationApplyResult(bool AnyApplied, string Message);

    // ─── Constructor ──────────────────────────────────────────────────────────
    public MainViewModel(
        IDisplayService displayService,
        IProcessMonitorService processMonitorService,
        IConfigurationService configurationService)
    {
        AppLogger.Info("MainViewModel constructor begin");
        _displaySvc = displayService;
        _monitorSvc = processMonitorService;
        _configSvc  = configurationService;

        _config = _configSvc.Load();
        _config.AppliedState ??= new AppliedDisplayState();
        AppLogger.Info(
            $"Configuration loaded. Profiles={_config.Profiles.Count}, MonitoringEnabled={_config.MonitoringEnabled}, AlwaysMonitor={_config.MonitorExeAlwaysEnabled}");

        // Load settings
        _startWithWindows        = _config.StartWithWindows;
        _minimizeToTray          = _config.MinimizeToTray;
        _startMinimized          = _config.StartMinimized;
        _monitorExeAlwaysEnabled = _config.MonitorExeAlwaysEnabled;

        // Populate monitors
        RefreshMonitors();
        AppLogger.Info($"Monitors loaded. Count={Monitors.Count}");

        // Populate profiles
        foreach (var p in _config.Profiles)
            Profiles.Add(new ProfileViewModel(p));
        BackfillMonitorStableIds();
        AppLogger.Info($"Profiles loaded. Count={Profiles.Count}");

        // Load available resolutions for primary monitor initially
        RefreshResolutions(null);
        AppLogger.Info($"Primary resolutions loaded. Count={AvailableResolutions.Count}, Current={CurrentResolution}");

        // Wire process events
        _monitorSvc.ProcessStarted += HandleProcessStarted;
        _monitorSvc.ProcessStopped += HandleProcessStopped;

        // Commands
        AddProfileCommand    = new RelayCommand(AddProfile);
        RemoveProfileCommand = new RelayCommand(RemoveProfile, () => SelectedProfile is not null);
        BrowseExeCommand     = new RelayCommand(BrowseExe,    () => SelectedProfile is not null);
        SaveCommand          = new RelayCommand(Save);
        ApplyNowCommand      = new RelayCommand(ApplyNow, CanApplyAnything);
        ApplyResolutionCommand = new RelayCommand(ApplyResolutionOnly,
            () => SelectedProfile?.TargetResolution is not null && IsResolutionEditable);
        ApplySaturationCommand = new RelayCommand(ApplySaturationOnly,
            () => SelectedProfile is not null && IsSaturationEditable);
        RevertNowCommand = new RelayCommand(RevertNow,
            () => SelectedProfile is not null && IsApplied);
        RevertResolutionCommand = new RelayCommand(RevertResolutionOnly,
            () => SelectedProfile is not null && _resolutionApplied);
        RevertSaturationCommand = new RelayCommand(RevertSaturationOnly,
            () => SelectedProfile is not null && IsNvidiaAvailable && _saturationApplied);
        ToggleMonitorCommand = new RelayCommand(ToggleMonitor);
        RedetectMonitorsCommand = new RelayCommand(RedetectMonitors);
        SaveSettingsCommand  = new RelayCommand(SaveSettings);
        FixNvidiaCommand     = new RelayCommand(FixNvidia);

        SelectedProfile = Profiles.FirstOrDefault();

        // Apply startup behaviour
        ApplyStartWithWindows(_startWithWindows);

        // Auto-start if persisted
        if (_config.MonitoringEnabled || _config.MonitorExeAlwaysEnabled)
            StartMonitor();

        AppLogger.Info("MainViewModel constructor complete");
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

            OnPropertyChanged(nameof(IsProfileEditable));
            OnPropertyChanged(nameof(IsResolutionEditable));
            OnPropertyChanged(nameof(IsSaturationEditable));
            OnPropertyChanged(nameof(IsMonitorSelectionEditable));

            // Reset UI state, then detect whether this profile is already active.
            SetAppliedState(resolutionApplied: false, saturationApplied: false);
            OnPropertyChanged(nameof(ShowVibranceWarning));

            // Sync monitor picker
            RunWithoutSelectionPersistence(() =>
            {
                DisplayMonitor? monitor = ResolveProfileMonitor(value);
                Set(ref _selectedMonitorForProfile, monitor, nameof(SelectedMonitorForProfile));
                if (value is not null
                    && !ProfileTargetsPrimary(value.TargetMonitorStableId, value.TargetMonitorDeviceName)
                    && MonitorMatchesProfile(value, monitor))
                {
                    UpdateProfileMonitorIdentity(value, monitor);
                }

                // Refresh resolutions for the profile's chosen monitor
                RefreshResolutions(monitor?.DeviceName);
                SyncResolutionSelectionsFromProfile();
                DetectAppliedState(value, monitor?.DeviceName);
            });

            RelayCommand.Refresh();
        }
    }

    public DisplayMonitor? SelectedMonitorForProfile
    {
        get => _selectedMonitorForProfile;
        set
        {
            if (!Set(ref _selectedMonitorForProfile, value)) return;
            if (_suppressSelectionPersistence) return;

            if (SelectedProfile is not null)
                UpdateProfileMonitorIdentity(SelectedProfile, value, clearWhenNull: true);

            // Refresh available resolutions for newly selected monitor
            RunWithoutSelectionPersistence(() => RefreshResolutions(value?.DeviceName));
            SelectedResolution = null;
            SelectedExitResolution = null;
        }
    }

    public DisplayResolution? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (!Set(ref _selectedResolution, value)) return;
            if (_suppressSelectionPersistence) return;

            if (SelectedProfile is not null)
                SelectedProfile.TargetResolution = value;
        }
    }

    public DisplayResolution? SelectedExitResolution
    {
        get => _selectedExitResolution;
        set
        {
            if (!Set(ref _selectedExitResolution, value)) return;
            if (_suppressSelectionPersistence) return;

            if (SelectedProfile is not null)
                SelectedProfile.ExitResolution = value;
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

    /// <summary>True after any manual apply action, enabling the manual revert button.</summary>
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
    public bool IsProfileEditable => SelectedProfile is not null;

    public bool IsResolutionEditable => SelectedProfile is not null && !_resolutionApplied;

    public bool IsSaturationEditable => SelectedProfile is not null && IsNvidiaAvailable && !_saturationApplied;

    public bool IsSaturationVisible => IsNvidiaAvailable;

    public bool IsMonitorSelectionEditable => SelectedProfile is not null && !IsApplied;

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
    public ICommand ApplyResolutionCommand { get; }
    public ICommand ApplySaturationCommand { get; }
    public ICommand RevertNowCommand { get; }
    public ICommand RevertResolutionCommand { get; }
    public ICommand RevertSaturationCommand { get; }
    public ICommand ToggleMonitorCommand { get; }
    public ICommand RedetectMonitorsCommand { get; }
    public ICommand SaveSettingsCommand  { get; }
    public ICommand FixNvidiaCommand     { get; }

    /// <summary>True when running on a system with NVIDIA NvAPI support.</summary>
    public bool IsNvidiaAvailable => NvApiService.IsAvailable;

    /// <summary>Human-readable NVIDIA status for display in the UI.</summary>
    public string NvApiStatus => NvApiService.IsAvailable
        ? "Digital Vibrance disponível"
        : $"Driver NVIDIA não detectado: {NvApiService.LastError}";

    /// <summary>Shown next to the vibrance toggle when NvAPI is not available.</summary>
    public bool ShowVibranceWarning => false;

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
        if (SelectedProfile is null) return;

        if (!TryResolveProfileDeviceName(SelectedProfile, out string? deviceName, out string? error))
        {
            StatusMessage = error;
            return;
        }

        bool resolutionOk = false;
        bool resolutionSkippedAsActive = !IsResolutionEditable;
        if (IsResolutionEditable)
            resolutionOk = ApplyResolution(SelectedProfile, deviceName);

        var saturation = IsNvidiaAvailable
            ? IsSaturationEditable
                ? ApplySaturation(SelectedProfile, deviceName)
                : new SaturationApplyResult(false, "Saturação: já aplicada")
            : new SaturationApplyResult(false, string.Empty);

        if (resolutionOk) MarkResolutionApplied();
        if (saturation.AnyApplied) MarkSaturationApplied();

        CurrentResolution = _displaySvc.GetCurrentResolution(deviceName);
        string resolutionInfo = SelectedProfile.TargetResolution is null
            ? "Resolução: ignorada"
            : resolutionSkippedAsActive
                ? "Resolução: já aplicada"
            : resolutionOk
                ? $"Resolução: {CurrentResolution}"
                : "Resolução falhou";
        StatusMessage = string.IsNullOrWhiteSpace(saturation.Message)
            ? resolutionInfo
            : $"{resolutionInfo} | {saturation.Message}";
    }

    private void ApplyResolutionOnly()
    {
        if (SelectedProfile is null) return;

        if (!TryResolveProfileDeviceName(SelectedProfile, out string? deviceName, out string? error))
        {
            StatusMessage = error;
            return;
        }

        bool ok = ApplyResolution(SelectedProfile, deviceName);
        CurrentResolution = _displaySvc.GetCurrentResolution(deviceName);
        if (ok) MarkResolutionApplied();

        StatusMessage = ok
            ? $"Resolução aplicada: {CurrentResolution}"
            : "Não foi possível aplicar a resolução selecionada.";
    }

    private void ApplySaturationOnly()
    {
        if (SelectedProfile is null) return;
        if (!IsNvidiaAvailable)
        {
            StatusMessage = "Saturação disponível apenas em placas NVIDIA.";
            return;
        }

        if (!TryResolveProfileDeviceName(SelectedProfile, out string? deviceName, out string? error))
        {
            StatusMessage = error;
            return;
        }

        var result = ApplySaturation(SelectedProfile, deviceName);
        if (result.AnyApplied)
            MarkSaturationApplied();

        StatusMessage = result.Message;
    }

    private void RevertNow()
    {
        if (SelectedProfile is null) return;

        if (!TryResolveProfileDeviceName(SelectedProfile, out string? deviceName, out string? error))
        {
            StatusMessage = error;
            return;
        }

        _displaySvc.RestoreResolution(deviceName, GetResolutionRestoreTarget(SelectedProfile));
        if (IsNvidiaAvailable || _saturationApplied)
        {
            RestoreSaturation(deviceName, SelectedProfile);
            _displaySvc.ResetExtraSaturation(deviceName);
        }
        CurrentResolution = _displaySvc.GetCurrentResolution(deviceName);
        DisplayResolution? restoredResolution = GetResolutionRestoreTarget(SelectedProfile);
        StatusMessage = restoredResolution is not null
            ? $"Resolução aplicada ao reverter: {restoredResolution}"
            : "Configurações revertidas.";
        ClearAppliedState();
    }

    private void RevertResolutionOnly()
    {
        if (SelectedProfile is null) return;

        if (!TryResolveProfileDeviceName(SelectedProfile, out string? deviceName, out string? error))
        {
            StatusMessage = error;
            return;
        }

        DisplayResolution? restoreTarget = GetResolutionRestoreTarget(SelectedProfile);
        bool ok = _displaySvc.RestoreResolution(deviceName, restoreTarget);
        CurrentResolution = _displaySvc.GetCurrentResolution(deviceName);
        _resolutionApplied = false;
        ClearPersistedAppliedFlag(resolution: true);
        RefreshAppliedState();

        StatusMessage = restoreTarget is not null
            ? $"Resolução aplicada ao reverter: {restoreTarget}"
            : ok
                ? $"Resolução restaurada: {CurrentResolution}"
                : "Nenhuma resolução aplicada para reverter.";
    }

    private void RevertSaturationOnly()
    {
        if (SelectedProfile is null) return;
        if (!IsNvidiaAvailable)
        {
            StatusMessage = "Saturação disponível apenas em placas NVIDIA.";
            return;
        }

        if (!TryResolveProfileDeviceName(SelectedProfile, out string? deviceName, out string? error))
        {
            StatusMessage = error;
            return;
        }

        RestoreSaturation(deviceName, SelectedProfile);
        _displaySvc.ResetExtraSaturation(deviceName);
        _saturationApplied = false;
        ClearPersistedAppliedFlag(saturation: true);
        RefreshAppliedState();
        StatusMessage = "Saturação revertida.";
    }

    private bool CanApplyAnything()
        => SelectedProfile is not null && (IsResolutionEditable || IsSaturationEditable);

    private bool ApplyResolution(ProfileViewModel profile, string? deviceName)
    {
        if (profile.TargetResolution is null)
            return false;

        DisplayResolution original = _displaySvc.GetCurrentResolution(deviceName);
        bool ok = _displaySvc.SetResolution(deviceName, profile.TargetResolution);
        if (ok)
            RememberResolutionApplied(profile, deviceName, original);

        return ok;
    }

    private SaturationApplyResult ApplySaturation(ProfileViewModel profile, string? deviceName)
    {
        if (!IsNvidiaAvailable)
            return new SaturationApplyResult(false, string.Empty);

        int? originalRawLevel = _displaySvc.GetCurrentVibranceRawLevel(deviceName);
        var result = ApplySaturation(
            deviceName,
            profile.VibranceEnabled,
            profile.DigitalVibrance,
            profile.ExtraSaturationEnabled,
            profile.ExtraSaturation);

        if (result.AnyApplied)
            RememberSaturationApplied(profile, deviceName, originalRawLevel);

        return result;
    }

    private SaturationApplyResult ApplySaturation(
        string? deviceName,
        bool vibranceEnabled,
        int digitalVibrance,
        bool extraSaturationEnabled,
        int extraSaturation)
    {
        if (!IsNvidiaAvailable)
            return new SaturationApplyResult(false, string.Empty);

        var parts = new List<string>();
        bool anyApplied = false;
        int baseVibrance = vibranceEnabled ? digitalVibrance : 0;
        int extraVibrance = extraSaturationEnabled ? extraSaturation : 0;
        int requestedVibrance = baseVibrance + extraVibrance;
        int effectiveVibrance = Math.Clamp(requestedVibrance, 0, 100);

        _displaySvc.ResetExtraSaturation(deviceName);

        if (vibranceEnabled || extraSaturationEnabled)
        {
            if (NvApiService.IsAvailable)
            {
                bool vibranceOk = _displaySvc.SetVibrance(deviceName, effectiveVibrance);
                WriteNvApiLog(vibranceOk);
                parts.Add(vibranceOk
                    ? BuildVibranceStatus(baseVibrance, extraVibrance, requestedVibrance, effectiveVibrance)
                    : $"Digital Vibrance não aplicado: {NvApiService.LastError}");
                anyApplied |= vibranceOk;
            }
            else
            {
                parts.Add("Digital Vibrance indisponível: driver NVIDIA/NvAPI não detectado");
            }
        }
        else
        {
            _displaySvc.RestoreVibrance(deviceName);
            parts.Add("Digital Vibrance: desligado");
        }

        int beyondDriverLimit = NvApiService.IsAvailable
            ? Math.Max(0, requestedVibrance - effectiveVibrance)
            : extraVibrance;

        if (beyondDriverLimit > 0)
        {
            bool extraOk = _displaySvc.SetExtraSaturation(deviceName, beyondDriverLimit);
            parts.Add(extraOk
                ? $"Extra além do limite: {beyondDriverLimit}%"
                : "Extra além do limite falhou: gamma ramp recusado");
            anyApplied |= extraOk;
        }

        return new SaturationApplyResult(anyApplied, string.Join(" | ", parts));
    }

    private static string BuildVibranceStatus(
        int baseVibrance,
        int extraVibrance,
        int requestedVibrance,
        int effectiveVibrance)
    {
        string status = extraVibrance > 0
            ? $"Digital Vibrance: {baseVibrance}% + Extra: {extraVibrance}% = {effectiveVibrance}%"
            : $"Digital Vibrance: {effectiveVibrance}%";

        if (requestedVibrance > effectiveVibrance)
            status += " (limite do driver)";

        return status;
    }

    private static void WriteNvApiLog(bool vibranceOk)
    {
        string logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResSync");
        System.IO.Directory.CreateDirectory(logDir);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(logDir, "nvapi.log"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | IsAvailable={NvApiService.IsAvailable} | ok={vibranceOk} | {NvApiService.LastError}\n");
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
        AppLogger.Info("StartMonitor requested");
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
        AppLogger.Info($"Process monitoring started. ActiveProfiles={active.Count}");
        IsMonitoring  = true;
        StatusMessage = $"Monitorando {active.Count} aplicativo(s)…";
        PersistConfig(monitoringEnabled: true);
    }

    private void StopMonitor()
    {
        AppLogger.Info("StopMonitor requested");
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
            if (!TryResolveProfileDeviceName(profile, out string? dev, out string? error))
            {
                StatusMessage = $"[{profile.Name}] {error}";
                return;
            }

            if (profile.TargetResolution is not null)
                _displaySvc.SetResolution(dev, profile.TargetResolution);

            var saturation = ApplySaturation(
                dev,
                profile.DigitalVibrance >= 0,
                profile.DigitalVibrance >= 0 ? profile.DigitalVibrance : 0,
                profile.ExtraSaturation >= 0,
                profile.ExtraSaturation >= 0 ? profile.ExtraSaturation : 0);

            CurrentResolution = _displaySvc.GetCurrentResolution(dev);
            string appliedText = profile.TargetResolution is not null
                ? profile.TargetResolution.ToString()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(saturation.Message))
                appliedText = string.IsNullOrWhiteSpace(appliedText)
                    ? saturation.Message
                    : $"{appliedText} | {saturation.Message}";

            StatusMessage = string.IsNullOrWhiteSpace(appliedText)
                ? $"[{profile.Name}] aberto"
                : $"[{profile.Name}] aberto → {appliedText}";
        });
    }

    private void HandleProcessStopped(object? sender, AppProfile profile)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!TryResolveProfileDeviceName(profile, out string? dev, out string? error))
            {
                StatusMessage = $"[{profile.Name}] {error}";
                return;
            }

            _displaySvc.RestoreResolution(dev, profile.ExitResolution);
            _displaySvc.RestoreVibrance(dev);
            _displaySvc.ResetExtraSaturation(dev);
            CurrentResolution = _displaySvc.GetCurrentResolution(dev);
            StatusMessage = profile.ExitResolution is not null
                ? $"[{profile.Name}] fechado → {profile.ExitResolution}"
                : $"[{profile.Name}] fechado → resolução restaurada";
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    public void RedetectMonitors()
        => RedetectMonitors(updateStatus: true);

    public void RefreshDisplayTopology()
        => RedetectMonitors(updateStatus: false);

    private void RedetectMonitors(bool updateStatus)
    {
        var profile = SelectedProfile;
        string? savedProfileMonitor = profile?.TargetMonitorDeviceName;
        string? savedProfileStableId = profile?.TargetMonitorStableId;
        var savedTargetResolution = profile?.TargetResolution;
        var savedExitResolution = profile?.ExitResolution;
        bool matchedSavedMonitor = false;

        RunWithoutSelectionPersistence(() =>
        {
            RefreshMonitors();

            DisplayMonitor? monitor = ResolveProfileMonitor(profile);
            matchedSavedMonitor = MonitorMatchesIdentity(savedProfileStableId, savedProfileMonitor, monitor);
            if (profile is not null
                && matchedSavedMonitor
                && !ProfileTargetsPrimary(savedProfileStableId, savedProfileMonitor))
            {
                UpdateProfileMonitorIdentity(profile, monitor);
            }

            Set(ref _selectedMonitorForProfile, monitor, nameof(SelectedMonitorForProfile));

            RefreshResolutions(monitor?.DeviceName);
            SyncResolutionSelectionsFromProfile();
            DetectAppliedState(profile, monitor?.DeviceName);
        });

        if (profile is not null)
        {
            // Preserve the saved profile values exactly. ComboBox collection refreshes
            // can briefly publish null selections, which must not erase the profile.
            if (!matchedSavedMonitor)
            {
                profile.TargetMonitorDeviceName = savedProfileMonitor;
                profile.TargetMonitorStableId = savedProfileStableId;
            }
            profile.TargetResolution = savedTargetResolution;
            profile.ExitResolution = savedExitResolution;
        }

        if (updateStatus)
            StatusMessage = $"Monitores redetectados: {Monitors.Count} (configurações preservadas)";
    }

    private void RunWithoutSelectionPersistence(Action action)
    {
        bool previous = _suppressSelectionPersistence;
        _suppressSelectionPersistence = true;
        try
        {
            action();
        }
        finally
        {
            _suppressSelectionPersistence = previous;
        }
    }

    private void RefreshMonitors()
    {
        AppLogger.Info("RefreshMonitors begin");
        Monitors.Clear();
        foreach (var m in _displaySvc.GetMonitors())
        {
            Monitors.Add(m);
            AppLogger.Info($"Monitor detected. Device={m.DeviceName}, StableId={m.StableId}, Description={m.Description}, Primary={m.IsPrimary}");
        }
        AppLogger.Info($"RefreshMonitors complete. Count={Monitors.Count}");
    }

    private DisplayMonitor? ResolveProfileMonitor(ProfileViewModel? profile)
        => ResolveProfileMonitor(profile, allowFallback: true);

    private DisplayMonitor? ResolveProfileMonitor(ProfileViewModel? profile, bool allowFallback)
        => ResolveMonitor(profile?.TargetMonitorStableId, profile?.TargetMonitorDeviceName, allowFallback);

    private DisplayMonitor? ResolveProfileMonitor(AppProfile? profile, bool allowFallback)
        => ResolveMonitor(profile?.TargetMonitorStableId, profile?.TargetMonitorDeviceName, allowFallback);

    private DisplayMonitor? ResolveMonitor(string? stableId, string? deviceName, bool allowFallback)
    {
        if (!string.IsNullOrWhiteSpace(stableId))
        {
            var monitor = Monitors.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(m.StableId)
                && m.StableId.Equals(stableId, StringComparison.OrdinalIgnoreCase));
            if (monitor is not null)
                return monitor;
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var monitor = Monitors.FirstOrDefault(m => m.DeviceName.Equals(
                deviceName, StringComparison.OrdinalIgnoreCase));
            if (monitor is not null)
                return monitor;
        }

        return allowFallback
            ? Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault()
            : null;
    }

    private bool TryResolveProfileDeviceName(ProfileViewModel profile, out string? deviceName, out string error)
    {
        if (ProfileTargetsPrimary(profile.TargetMonitorStableId, profile.TargetMonitorDeviceName))
        {
            deviceName = null;
            error = string.Empty;
            return true;
        }

        DisplayMonitor? monitor = ResolveProfileMonitor(profile, allowFallback: false);
        if (monitor is null)
        {
            deviceName = null;
            error = "Monitor alvo salvo não encontrado. Use Redetectar e selecione o monitor novamente.";
            return false;
        }

        UpdateProfileMonitorIdentity(profile, monitor);
        deviceName = monitor.DeviceName;
        error = string.Empty;
        return true;
    }

    private bool TryResolveProfileDeviceName(AppProfile profile, out string? deviceName, out string error)
    {
        if (ProfileTargetsPrimary(profile.TargetMonitorStableId, profile.TargetMonitorDeviceName))
        {
            deviceName = null;
            error = string.Empty;
            return true;
        }

        DisplayMonitor? monitor = ResolveProfileMonitor(profile, allowFallback: false);
        if (monitor is null)
        {
            deviceName = null;
            error = "Monitor alvo salvo não encontrado. Use Redetectar e selecione o monitor novamente.";
            return false;
        }

        UpdateProfileMonitorIdentity(profile, monitor);
        deviceName = monitor.DeviceName;
        error = string.Empty;
        return true;
    }

    private static bool ProfileTargetsPrimary(string? stableId, string? deviceName)
        => string.IsNullOrWhiteSpace(stableId) && string.IsNullOrWhiteSpace(deviceName);

    private static bool MonitorMatchesProfile(ProfileViewModel profile, DisplayMonitor? monitor)
        => MonitorMatchesIdentity(profile.TargetMonitorStableId, profile.TargetMonitorDeviceName, monitor);

    private static bool MonitorMatchesIdentity(string? stableId, string? deviceName, DisplayMonitor? monitor)
    {
        if (monitor is null)
            return false;

        if (!string.IsNullOrWhiteSpace(stableId)
            && !string.IsNullOrWhiteSpace(monitor.StableId))
        {
            return monitor.StableId.Equals(stableId, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
            return monitor.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase);

        return string.IsNullOrWhiteSpace(stableId) && monitor.IsPrimary;
    }

    private static void UpdateProfileMonitorIdentity(
        ProfileViewModel profile,
        DisplayMonitor? monitor,
        bool clearWhenNull = false)
    {
        if (monitor is null)
        {
            if (clearWhenNull)
            {
                profile.TargetMonitorDeviceName = null;
                profile.TargetMonitorStableId = null;
            }
            return;
        }

        profile.TargetMonitorDeviceName = monitor.DeviceName;
        if (!string.IsNullOrWhiteSpace(monitor.StableId))
            profile.TargetMonitorStableId = monitor.StableId;
    }

    private static void UpdateProfileMonitorIdentity(AppProfile profile, DisplayMonitor monitor)
    {
        profile.TargetMonitorDeviceName = monitor.DeviceName;
        if (!string.IsNullOrWhiteSpace(monitor.StableId))
            profile.TargetMonitorStableId = monitor.StableId;
    }

    private void BackfillMonitorStableIds()
    {
        bool changed = false;

        foreach (var profile in Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.TargetMonitorStableId)
                || string.IsNullOrWhiteSpace(profile.TargetMonitorDeviceName))
            {
                continue;
            }

            DisplayMonitor? monitor = ResolveProfileMonitor(profile, allowFallback: false);
            if (monitor is null || string.IsNullOrWhiteSpace(monitor.StableId))
                continue;

            profile.TargetMonitorStableId = monitor.StableId;
            changed = true;
        }

        if (!changed)
            return;

        _config.Profiles = [.. Profiles.Select(p => p.GetModel())];
        _configSvc.Save(_config);
    }

    private void DetectAppliedState(ProfileViewModel? profile, string? deviceName)
    {
        if (profile is null)
        {
            SetAppliedState(resolutionApplied: false, saturationApplied: false);
            return;
        }

        bool resolutionApplied = IsPersistedResolutionActive(profile, deviceName)
            || IsResolutionProbablyActive(profile);

        bool saturationApplied = IsNvidiaAvailable
            && (IsPersistedSaturationActive(profile, deviceName)
                || IsSaturationProbablyActive(profile, deviceName));

        SetAppliedState(resolutionApplied, saturationApplied);
    }

    private bool IsPersistedResolutionActive(ProfileViewModel profile, string? deviceName)
    {
        AppliedDisplayState state = _config.AppliedState;
        if (!state.ResolutionApplied || !PersistedStateMatchesProfile(state, profile, deviceName))
            return false;

        return profile.TargetResolution is not null
            && CurrentResolution?.Equals(profile.TargetResolution) == true;
    }

    private bool IsPersistedSaturationActive(ProfileViewModel profile, string? deviceName)
    {
        if (!IsNvidiaAvailable)
            return false;

        AppliedDisplayState state = _config.AppliedState;
        if (!state.SaturationApplied || !PersistedStateMatchesProfile(state, profile, deviceName))
            return false;

        if (_displaySvc.IsExtraSaturationActive(deviceName))
            return true;

        int? currentRawLevel = _displaySvc.GetCurrentVibranceRawLevel(deviceName);
        return state.OriginalVibranceRawLevel is null
            || currentRawLevel is null
            || currentRawLevel.Value != state.OriginalVibranceRawLevel.Value;
    }

    private bool IsResolutionProbablyActive(ProfileViewModel profile)
    {
        if (profile.TargetResolution is null || CurrentResolution is null)
            return false;

        if (!CurrentResolution.Equals(profile.TargetResolution))
            return false;

        return profile.ExitResolution is not null && !CurrentResolution.Equals(profile.ExitResolution);
    }

    private bool IsSaturationProbablyActive(ProfileViewModel profile, string? deviceName)
    {
        if (!IsNvidiaAvailable)
            return false;

        int requestedVibrance = GetRequestedVibrance(profile);
        bool profileUsesSaturation = profile.VibranceEnabled || profile.ExtraSaturationEnabled;
        if (!profileUsesSaturation)
            return false;

        int? currentVibrance = _displaySvc.GetCurrentVibrance(deviceName);
        if (currentVibrance is not null && Math.Abs(currentVibrance.Value - Math.Clamp(requestedVibrance, 0, 100)) <= 2)
            return true;

        return profile.ExtraSaturationEnabled && _displaySvc.IsExtraSaturationActive(deviceName);
    }

    private static int GetRequestedVibrance(ProfileViewModel profile)
    {
        int baseVibrance = profile.VibranceEnabled ? profile.DigitalVibrance : 0;
        int extraVibrance = profile.ExtraSaturationEnabled ? profile.ExtraSaturation : 0;
        return Math.Clamp(baseVibrance + extraVibrance, 0, 100);
    }

    private static bool PersistedStateMatchesProfile(
        AppliedDisplayState state,
        ProfileViewModel profile,
        string? deviceName)
    {
        if (!string.IsNullOrWhiteSpace(state.ProfileId)
            && !state.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(state.TargetMonitorStableId)
            && !string.IsNullOrWhiteSpace(profile.TargetMonitorStableId))
        {
            return state.TargetMonitorStableId.Equals(profile.TargetMonitorStableId, StringComparison.OrdinalIgnoreCase);
        }

        string? effectiveDeviceName = deviceName ?? profile.TargetMonitorDeviceName;
        if (!string.IsNullOrWhiteSpace(state.TargetMonitorDeviceName)
            && !string.IsNullOrWhiteSpace(effectiveDeviceName))
        {
            return state.TargetMonitorDeviceName.Equals(effectiveDeviceName, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrWhiteSpace(state.TargetMonitorStableId)
            && string.IsNullOrWhiteSpace(state.TargetMonitorDeviceName)
            && ProfileTargetsPrimary(profile.TargetMonitorStableId, profile.TargetMonitorDeviceName);
    }

    private DisplayResolution? GetResolutionRestoreTarget(ProfileViewModel profile)
    {
        if (profile.ExitResolution is not null)
            return profile.ExitResolution;

        return PersistedStateMatchesProfile(_config.AppliedState, profile, profile.TargetMonitorDeviceName)
            ? _config.AppliedState.OriginalResolution
            : null;
    }

    private void RestoreSaturation(string? deviceName, ProfileViewModel profile)
    {
        if (!IsNvidiaAvailable)
            return;

        if (_displaySvc.RestoreVibrance(deviceName))
            return;

        AppliedDisplayState state = _config.AppliedState;
        if (PersistedStateMatchesProfile(state, profile, deviceName)
            && state.OriginalVibranceRawLevel is int rawLevel
            && _displaySvc.RestoreVibranceLevel(deviceName, rawLevel))
        {
            return;
        }

        _displaySvc.SetVibrance(deviceName, 0);
    }

    private void RememberResolutionApplied(ProfileViewModel profile, string? deviceName, DisplayResolution original)
    {
        AppliedDisplayState state = PrepareAppliedState(profile, deviceName);
        state.OriginalResolution ??= original;
        state.ResolutionApplied = true;
        state.UpdatedAt = DateTime.Now;
        _configSvc.Save(_config);
    }

    private void RememberSaturationApplied(ProfileViewModel profile, string? deviceName, int? originalRawLevel)
    {
        AppliedDisplayState state = PrepareAppliedState(profile, deviceName);
        state.OriginalVibranceRawLevel ??= originalRawLevel;
        state.SaturationApplied = true;
        state.UpdatedAt = DateTime.Now;
        _configSvc.Save(_config);
    }

    private AppliedDisplayState PrepareAppliedState(ProfileViewModel profile, string? deviceName)
    {
        AppliedDisplayState state = _config.AppliedState;
        if (!PersistedStateMatchesProfile(state, profile, deviceName)
            || (!state.ResolutionApplied && !state.SaturationApplied))
        {
            state.ProfileId = profile.Id;
            state.TargetMonitorDeviceName = deviceName;
            state.TargetMonitorStableId = profile.TargetMonitorStableId;
            state.OriginalResolution = null;
            state.OriginalVibranceRawLevel = null;
            state.ResolutionApplied = false;
            state.SaturationApplied = false;
        }

        return state;
    }

    private void ClearPersistedAppliedFlag(bool resolution = false, bool saturation = false)
    {
        if (resolution)
            _config.AppliedState.ResolutionApplied = false;

        if (saturation)
            _config.AppliedState.SaturationApplied = false;

        if (!_config.AppliedState.ResolutionApplied && !_config.AppliedState.SaturationApplied)
            _config.AppliedState = new AppliedDisplayState();

        _configSvc.Save(_config);
    }

    public void ClearPersistedAppliedState()
    {
        _config.AppliedState = new AppliedDisplayState();
        SetAppliedState(resolutionApplied: false, saturationApplied: false);
        _configSvc.Save(_config);
    }

    public void RestorePersistedAppliedState()
    {
        AppliedDisplayState state = _config.AppliedState;
        if (!state.ResolutionApplied && !state.SaturationApplied)
            return;

        ProfileViewModel? profile = Profiles.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(state.ProfileId)
            && p.Id.Equals(state.ProfileId, StringComparison.OrdinalIgnoreCase));

        DisplayMonitor? monitor = ResolveMonitor(
            state.TargetMonitorStableId,
            state.TargetMonitorDeviceName,
            allowFallback: false);
        string? deviceName = monitor?.DeviceName ?? state.TargetMonitorDeviceName;

        if (state.ResolutionApplied)
        {
            DisplayResolution? target = profile?.ExitResolution ?? state.OriginalResolution;
            if (target is not null)
                _displaySvc.RestoreResolution(deviceName, target);
        }

        if (state.SaturationApplied)
        {
            if (IsNvidiaAvailable)
            {
                if (state.OriginalVibranceRawLevel is int rawLevel)
                    _displaySvc.RestoreVibranceLevel(deviceName, rawLevel);
                else
                    _displaySvc.SetVibrance(deviceName, 0);
            }

            _displaySvc.ResetExtraSaturation(deviceName);
        }

        ClearPersistedAppliedState();
    }

    private void MarkResolutionApplied()
    {
        _resolutionApplied = true;
        RefreshAppliedState();
    }

    private void MarkSaturationApplied()
    {
        _saturationApplied = true;
        RefreshAppliedState();
    }

    private void ClearAppliedState()
    {
        _config.AppliedState = new AppliedDisplayState();
        _configSvc.Save(_config);
        SetAppliedState(resolutionApplied: false, saturationApplied: false);
    }

    private void SetAppliedState(bool resolutionApplied, bool saturationApplied)
    {
        _resolutionApplied = resolutionApplied;
        _saturationApplied = saturationApplied;
        RefreshAppliedState();
    }

    private void RefreshAppliedState()
    {
        bool isApplied = _resolutionApplied || _saturationApplied;
        if (_isApplied != isApplied)
        {
            _isApplied = isApplied;
            OnPropertyChanged(nameof(IsApplied));
            OnPropertyChanged(nameof(IsProfileEditable));
            OnPropertyChanged(nameof(IsResolutionEditable));
            OnPropertyChanged(nameof(IsSaturationEditable));
            OnPropertyChanged(nameof(IsMonitorSelectionEditable));
            OnPropertyChanged(nameof(ShowVibranceWarning));
        }

        OnPropertyChanged(nameof(IsResolutionEditable));
        OnPropertyChanged(nameof(IsSaturationEditable));
        OnPropertyChanged(nameof(IsMonitorSelectionEditable));

        RelayCommand.Refresh();
    }

    private void SyncResolutionSelectionsFromProfile()
    {
        Set(ref _selectedResolution,
            ResolveAvailableResolution(SelectedProfile?.TargetResolution),
            nameof(SelectedResolution));

        Set(ref _selectedExitResolution,
            ResolveAvailableResolution(SelectedProfile?.ExitResolution),
            nameof(SelectedExitResolution));
    }

    private DisplayResolution? ResolveAvailableResolution(DisplayResolution? resolution)
        => resolution is null
            ? null
            : AvailableResolutions.FirstOrDefault(r => r.Equals(resolution));

    private void RefreshResolutions(string? deviceName)
    {
        AppLogger.Info($"RefreshResolutions begin. Device={deviceName ?? "<primary>"}");
        AvailableResolutions.Clear();
        foreach (var r in _displaySvc.GetAvailableResolutions(deviceName))
            AvailableResolutions.Add(r);
        CurrentResolution = _displaySvc.GetCurrentResolution(deviceName);
        AppLogger.Info($"RefreshResolutions complete. Count={AvailableResolutions.Count}, Current={CurrentResolution}");
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
            if (exe is not null) key.SetValue(valueName, $"\"{exe}\" --startup");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

