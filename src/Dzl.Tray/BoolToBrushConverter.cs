using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Dzl.Tray;

/// <summary>Maps a bool to one of two configured brushes: <see cref="True"/> when the value is
/// <c>true</c>, else <see cref="False"/>. One converter, configured per resource instance — e.g. the
/// status-dot pills (accent-green / muted-grey) and the CE flag glyphs (green / muted) are both this
/// with different colors. Declare instances in App.xaml so UserControls resolve them (UserControls
/// never see host-window resources).</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    /// <summary>Brush returned when the bound value is <c>true</c>.</summary>
    public Brush? True { get; set; }

    /// <summary>Brush returned when the bound value is anything else.</summary>
    public Brush? False { get; set; }

    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? True : False;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
