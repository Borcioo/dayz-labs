using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Dzl.Tray;

/// <summary>Highlights a flag glyph: foreground accent when the bound bool is true, muted when false.
/// Drives the compact Flags column letters in the grid (C H M P · Cr De).</summary>
public sealed class FlagBrushConverter : IValueConverter
{
    private static readonly Brush On = Freeze("#7CE3A1");   // green — active flag
    private static readonly Brush Off = Freeze("#555B63");  // muted — inactive flag

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? On : Off;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>Visibility = Collapsed when the bound object is null (no row selected), else Visible.
/// The detail panel uses this (and its inverse) to swap between the editor and the placeholder.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visibility = Visible when the bound object is null (no row selected), else Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
