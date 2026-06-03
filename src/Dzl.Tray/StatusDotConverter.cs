using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Dzl.Tray;

/// <summary>
/// Maps a bool "is up" flag to a status-dot brush: accent-green when up, muted grey
/// when down. Used by the SERVER/CLIENT status pills in the top action bar.
/// </summary>
public sealed class StatusDotConverter : IValueConverter
{
    private static readonly SolidColorBrush Up = new(Color.FromRgb(0x4C, 0xAF, 0x50));   // green
    private static readonly SolidColorBrush Down = new(Color.FromRgb(0x6E, 0x6E, 0x6E)); // grey

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Up : Down;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
