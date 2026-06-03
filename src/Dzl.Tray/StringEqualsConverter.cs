using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Dzl.Tray;

/// <summary>
/// Compares the bound string to the ConverterParameter. Returns <c>true</c> when equal
/// (for RadioButton/ToggleButton IsChecked), used to drive the Logs view-mode selector.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object? ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        // Only the checked radio reports back; map it to its mode string.
        => value is true ? parameter : Binding.DoNothing;
}

/// <summary>
/// Like <see cref="StringEqualsConverter"/> but yields a <see cref="Visibility"/>:
/// Visible when the bound string equals the parameter, else Collapsed. Drives which of the
/// four Logs layout hosts is shown.
/// </summary>
public sealed class StringEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Multi-value converter: Visible when the two bound strings are equal, else Collapsed.
/// Used to mark the active profile (item name vs. ActivePreset) in the Profiles list.
/// </summary>
public sealed class StringMatchVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Length == 2 && string.Equals(values[0] as string, values[1] as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
