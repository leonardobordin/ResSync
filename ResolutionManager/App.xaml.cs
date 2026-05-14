using System.Windows;
using ResolutionManager.Services;
using ResolutionManager.ViewModels;
using ResolutionManager.Views;

namespace ResolutionManager;

public partial class App
{
    private readonly IProcessMonitorService _monitorService;
    private readonly IDisplayService        _displayService;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Views.MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    public App()
    {
        AppLogger.Info("App constructor begin");
        _monitorService = new ProcessMonitorService();
        _displayService = new DisplayService();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLogger.Exception("AppDomain unhandled exception", ex);
            else
                AppLogger.Error($"AppDomain unhandled exception object: {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Exception("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Exception("Dispatcher unhandled exception", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "ResSync — Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        AppLogger.Info("App constructor complete");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogger.Info($"OnStartup begin. Args=[{string.Join(", ", e.Args)}]");

        try
        {
            base.OnStartup(e);
            AppLogger.Info("WPF base startup complete");

            var configService = new ConfigurationService();
            AppLogger.Info($"Config service ready. Path={configService.ConfigFilePath}");

            _viewModel = new MainViewModel(_displayService, _monitorService, configService);
            AppLogger.Info(
                $"ViewModel ready. Profiles={_viewModel.Profiles.Count}, StartMinimized={_viewModel.StartMinimized}, MinimizeToTray={_viewModel.MinimizeToTray}");

            _mainWindow = new Views.MainWindow(_viewModel);
            AppLogger.Info("MainWindow created");

            TryCreateTrayIcon();

            // Only Windows startup launches should honor StartMinimized. A manual click
            // should always show the main window so the app never feels like it failed to open.
            bool launchedFromStartup = e.Args.Any(arg =>
                arg.Equals("--startup", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            AppLogger.Info($"Launch mode. StartupArg={launchedFromStartup}, TrayAvailable={CanUseTray}");

            if (_viewModel.StartMinimized && launchedFromStartup && CanUseTray)
            {
                AppLogger.Info("Starting minimized to tray");
                MinimizeToTray();
            }
            else
            {
                AppLogger.Info("Showing main window");
                ShowMainWindow();
            }

            AppLogger.Info("OnStartup complete");
        }
        catch (Exception ex)
        {
            AppLogger.Exception("OnStartup failed", ex);
            MessageBox.Show(
                $"O ResSync falhou ao iniciar.\n\nLog: {AppLogger.AppLogPath}\n\n{ex}",
                "ResSync — Erro ao iniciar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public bool CanUseTray => _trayIcon is not null;

    private void TryCreateTrayIcon()
    {
        try
        {
            AppLogger.Info("Creating tray icon");
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "ResSync",
                Visible = false
            };

            try
            {
                var iconUri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
                var sri = GetResourceStream(iconUri);
                if (sri?.Stream is not null)
                {
                    using var iconStream = sri.Stream;
                    _trayIcon.Icon = new System.Drawing.Icon(iconStream);
                    AppLogger.Info("Tray icon loaded from resource");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Tray icon resource load failed: {ex.Message}");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string icoPath = System.IO.Path.Combine(exeDir, "app.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    _trayIcon.Icon = new System.Drawing.Icon(icoPath);
                    AppLogger.Info($"Tray icon loaded from disk: {icoPath}");
                }
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Abrir ResSync", null, (_, _) => ShowMainWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Sair", null, (_, _) => ExitApplication());
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
            AppLogger.Info("Tray icon ready");
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Tray icon creation failed; continuing without tray", ex);
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
    }

    /// <summary>Hides window to tray (no exit).</summary>
    public void MinimizeToTray()
    {
        if (_trayIcon is null)
        {
            AppLogger.Warn("MinimizeToTray requested, but tray is unavailable. Showing window instead.");
            ShowMainWindow();
            return;
        }

        AppLogger.Info("Minimizing to tray");
        _mainWindow?.Hide();
        _trayIcon.Visible = true;
    }

    /// <summary>Brings the window back from the tray.</summary>
    public void ShowMainWindow()
    {
        AppLogger.Info("ShowMainWindow requested");
        if (_trayIcon is not null)
            _trayIcon.Visible = false;

        if (_mainWindow is not null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    /// <summary>Actually exits the application (from tray menu or when tray is disabled).</summary>
    public void ExitApplication()
    {
        // Mark that we truly want to close
        AppLogger.Info("ExitApplication requested");
        _isExiting = true;
        _mainWindow?.Close();
    }

    internal bool IsExiting => _isExiting;
    private bool _isExiting;

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info($"OnExit begin. Code={e.ApplicationExitCode}");
        try
        {
            _monitorService.StopMonitoring();
            _viewModel?.RestorePersistedAppliedState();
            foreach (var monitor in _displayService.GetMonitors())
            {
                _displayService.RestoreResolution(monitor.DeviceName);
                _displayService.RestoreVibrance(monitor.DeviceName);
                _displayService.ResetExtraSaturation(monitor.DeviceName);
            }
            _monitorService.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Display restore during exit failed", ex);
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        AppLogger.Info("OnExit complete");
        base.OnExit(e);
    }
}


