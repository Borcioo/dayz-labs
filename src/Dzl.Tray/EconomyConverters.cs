using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Dzl.Tray;

/// <summary>Validates a CE numeric field. Rejects non-integers and (unless <see cref="AllowNegative"/>)
/// negatives, so the binding never commits a bad value and the field shows its invalid (red) state.
/// Used by the form-based <see cref="Controls.TypeDetailPanel"/> numeric TextBoxes.</summary>
public sealed class NonNegativeIntRule : ValidationRule
{
    /// <summary>When true, negative integers are accepted (e.g. quantmin/quantmax use -1 = "not set").</summary>
    public bool AllowNegative { get; set; }

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        var s = (value as string)?.Trim() ?? "";
        if (s.Length == 0) return new ValidationResult(false, "required");
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && !int.TryParse(s, NumberStyles.Integer, cultureInfo, out n))
            return new ValidationResult(false, "must be a whole number");
        if (!AllowNegative && n < 0) return new ValidationResult(false, "must be ≥ 0");
        return ValidationResult.ValidResult;
    }
}

// The compact Flags column glyphs (active green / muted) now use the shared BoolToBrushConverter
// (declared in App.xaml as "FlagBrush"); the dedicated FlagBrushConverter was retired.

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
