using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using ResolutionManager.ViewModels;

namespace ResolutionManager.Views;

public partial class MainWindow : Window
{
    private const int WM_DISPLAYCHANGE = 0x007E;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE && DataContext is MainViewModel vm)
            vm.RefreshDisplayTopology();

        return IntPtr.Zero;
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
        if (DataContext is MainViewModel vm && vm.MinimizeToTray && app.CanUseTray)
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

    private void OpenButtonMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}

