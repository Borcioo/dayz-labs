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

/// <summary>Maps the bound container's <c>ActualWidth</c> to a responsive column count: 2 when wide enough,
/// else 1 (stacked). Lets the Spawnable Types cargo/attachments sections sit side by side on a wide pane and
/// stack on a narrow one without a SizeChanged handler.</summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    /// <summary>Below this width (device-independent px) the layout collapses to a single column.</summary>
    public double Threshold { get; set; } = 720;

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is double w && w >= Threshold ? 2 : 1;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a container's <c>ActualWidth</c> to a 4 / 2 / 1 responsive column count for the four CE
/// base-dictionary cards: 4 across on a wide window, 2x2 when medium, single column when narrow.</summary>
public sealed class WidthToColumns4Converter : IValueConverter
{
    /// <summary>At/above this width → 4 columns.</summary>
    public double Wide { get; set; } = 1000;
    /// <summary>At/above this width (but below <see cref="Wide"/>) → 2 columns; below → 1.</summary>
    public double Medium { get; set; } = 520;

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is double w ? (w >= Wide ? 4 : w >= Medium ? 2 : 1) : 4;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visibility = Visible when the bound count is 0 (drives an empty-state hint), else Collapsed.</summary>
public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
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
