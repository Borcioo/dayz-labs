using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Dzl.Tray;

/// <summary>true → Collapsed, false → Visible. Used to show an action (e.g. "Mount P:") only when a
/// boolean state is false (drive not mounted).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible ? false : true;
}
