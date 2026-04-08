using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ResolutionManager.ViewModels;

namespace ResolutionManager.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // ─── Close-to-tray logic ──────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        var app = (App)Application.Current;

        // If the app is truly exiting (tray menu "Sair"), let it close
        if (app.IsExiting)
        {
            base.OnClosing(e);
            return;
        }

        // If "Minimizar para a bandeja" is enabled, hide to tray instead of closing
        if (DataContext is MainViewModel vm && vm.MinimizeToTray)
        {
            e.Cancel = true;
            app.MinimizeToTray();
            return;
        }

        base.OnClosing(e);
    }

    // ─── Custom title-bar controls ────────────────────────────────────────────

    private void MinimizeWindow(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeRestore(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e)
        => Close();

    // ─── Tab navigation ───────────────────────────────────────────────────────

    private void ShowTabProfiles(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedTabIndex = 0;
    }

    private void ShowTabSettings(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedTabIndex = 1;
    }
}

