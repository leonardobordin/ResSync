using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ResolutionManager.Helpers;

/// <summary>Returns Visibility.Visible when value is not null, Collapsed otherwise.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns Visibility.Collapsed when value is not null, Visible otherwise.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a bool to one of two Brush values (true = AccentBrush, false = MutedBrush).</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public WpfBrush TrueBrush { get; set; } = new WpfSolidBrush(Color.FromRgb(0x00, 0xFF, 0x88));
    public WpfBrush FalseBrush { get; set; } = new WpfSolidBrush(Color.FromRgb(0x55, 0x55, 0x66));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true when value is not null.</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
