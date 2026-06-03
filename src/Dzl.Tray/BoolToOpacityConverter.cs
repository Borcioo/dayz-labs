using System.Globalization;
using System.Windows.Data;

namespace Dzl.Tray;

/// <summary>
/// Maps a bool to an opacity: <c>true</c> → 1.0 (full), <c>false</c> → 0.45 (dimmed).
/// Used on the Tools page to dim cards for tools that aren't installed.
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.45;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
