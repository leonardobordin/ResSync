// Resolve ambiguities between WPF and WinForms namespaces.
// The project uses WPF as primary UI but references WinForms for NotifyIcon (system tray).
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using Color = System.Windows.Media.Color;
