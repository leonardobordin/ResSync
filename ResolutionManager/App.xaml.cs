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
        _monitorService = new ProcessMonitorService();
        _displayService = new DisplayService();
        DispatcherUnhandledException += (_, args) =>
        {
            string logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ResSync", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.WriteAllText(logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{args.Exception}\n");
            MessageBox.Show(args.Exception.ToString(), "ResSync — Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configService = new ConfigurationService();
        _viewModel  = new MainViewModel(_displayService, _monitorService, configService);
        _mainWindow = new Views.MainWindow(_viewModel);

        // System tray icon
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text    = "ResSync",
            Visible = false
        };

        // Load the embedded app.ico
        try
        {
            var iconUri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
            var sri = GetResourceStream(iconUri);
            if (sri?.Stream is not null)
            {
                using var iconStream = sri.Stream;
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        }
        catch
        {
            // If resource fails, try loading from disk next to the exe
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string icoPath = System.IO.Path.Combine(exeDir, "app.ico");
            if (System.IO.File.Exists(icoPath))
                _trayIcon.Icon = new System.Drawing.Icon(icoPath);
        }

        // Context menu for the tray icon
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir ResSync", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        // If StartMinimized is on, go directly to tray without showing the window
        if (_viewModel.StartMinimized)
            MinimizeToTray();
        else
            _mainWindow.Show();
    }

    /// <summary>Hides window to tray (no exit).</summary>
    public void MinimizeToTray()
    {
        _mainWindow?.Hide();
        if (_trayIcon is not null)
            _trayIcon.Visible = true;
    }

    /// <summary>Brings the window back from the tray.</summary>
    public void ShowMainWindow()
    {
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
        _isExiting = true;
        _mainWindow?.Close();
    }

    internal bool IsExiting => _isExiting;
    private bool _isExiting;

    protected override void OnExit(ExitEventArgs e)
    {
        _monitorService.StopMonitoring();
        foreach (var monitor in _displayService.GetMonitors())
        {
            _displayService.RestoreResolution(monitor.DeviceName);
            _displayService.RestoreVibrance(monitor.DeviceName);
        }
        _monitorService.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }
}


