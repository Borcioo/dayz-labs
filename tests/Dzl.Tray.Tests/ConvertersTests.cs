using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Dzl.Tray;
using FluentAssertions;

/// <summary>Unit tests for the Tray's pure value converters + the CE numeric ValidationRule. No WPF runtime
/// needed — these are plain Convert/Validate calls.</summary>
public class ConvertersTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── NonNegativeIntRule ───────────────────────────────────────────────
    [Theory]
    [InlineData("", false)]        // required
    [InlineData("  ", false)]      // blank after trim
    [InlineData("abc", false)]     // not a number
    [InlineData("1.5", false)]     // not a whole number
    [InlineData("-1", false)]      // negative rejected by default
    [InlineData("0", true)]
    [InlineData("42", true)]
    [InlineData(" 7 ", true)]      // trimmed
    public void NonNegativeIntRule_default_rejects_blank_nonint_and_negative(string input, bool expectedValid)
    {
        new NonNegativeIntRule().Validate(input, Inv).IsValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("-1", true)]       // -1 = "not set" sentinel (quantmin/quantmax)
    [InlineData("-50", true)]
    [InlineData("3", true)]
    [InlineData("x", false)]       // still must be an integer
    public void NonNegativeIntRule_with_AllowNegative_accepts_negatives_but_still_requires_int(string input, bool expectedValid)
    {
        new NonNegativeIntRule { AllowNegative = true }.Validate(input, Inv).IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void NonNegativeIntRule_messages_are_specific()
    {
        var rule = new NonNegativeIntRule();
        rule.Validate("", Inv).ErrorContent.Should().Be("required");
        rule.Validate("abc", Inv).ErrorContent.Should().Be("must be a whole number");
        rule.Validate("-1", Inv).ErrorContent.Should().Be("must be ≥ 0");
    }

    // ── BoolToOpacityConverter ───────────────────────────────────────────
    [Fact]
    public void BoolToOpacity_maps_true_full_else_dim()
    {
        var c = new BoolToOpacityConverter();
        c.Convert(true, typeof(double), null, Inv).Should().Be(1.0);
        c.Convert(false, typeof(double), null, Inv).Should().Be(0.45);
        c.Convert(null!, typeof(double), null, Inv).Should().Be(0.45, "non-true is dimmed");
    }

    // ── BoolToBrushConverter ─────────────────────────────────────────────
    [Fact]
    public void BoolToBrush_returns_the_configured_brush_per_value()
    {
        var c = new BoolToBrushConverter { True = Brushes.Green, False = Brushes.Gray };
        c.Convert(true, typeof(Brush), null, Inv).Should().BeSameAs(Brushes.Green);
        c.Convert(false, typeof(Brush), null, Inv).Should().BeSameAs(Brushes.Gray);
        c.Convert(null!, typeof(Brush), null, Inv).Should().BeSameAs(Brushes.Gray, "non-true uses the False brush");
    }

    // ── InverseBoolToVisibilityConverter ─────────────────────────────────
    [Fact]
    public void InverseBoolToVisibility_true_collapses_false_shows_and_round_trips()
    {
        var c = new InverseBoolToVisibilityConverter();
        c.Convert(true, typeof(Visibility), null!, Inv).Should().Be(Visibility.Collapsed);
        c.Convert(false, typeof(Visibility), null!, Inv).Should().Be(Visibility.Visible);
        c.ConvertBack(Visibility.Visible, typeof(bool), null!, Inv).Should().Be(false);
        c.ConvertBack(Visibility.Collapsed, typeof(bool), null!, Inv).Should().Be(true);
    }

    // ── StringEqualsConverter ────────────────────────────────────────────
    [Fact]
    public void StringEquals_is_ordinal_and_round_trips_the_checked_radio()
    {
        var c = new StringEqualsConverter();
        c.Convert("tail", typeof(bool), "tail", Inv).Should().Be(true);
        c.Convert("Tail", typeof(bool), "tail", Inv).Should().Be(false, "comparison is case-sensitive (Ordinal)");
        c.Convert("split", typeof(bool), "tail", Inv).Should().Be(false);

        c.ConvertBack(true, typeof(string), "tail", Inv).Should().Be("tail");
        c.ConvertBack(false, typeof(string), "tail", Inv).Should().BeSameAs(Binding.DoNothing,
            "only the checked radio reports its mode back");
    }

    // ── StringEqualsVisibilityConverter ──────────────────────────────────
    [Fact]
    public void StringEqualsVisibility_visible_only_on_match()
    {
        var c = new StringEqualsVisibilityConverter();
        c.Convert("a", typeof(Visibility), "a", Inv).Should().Be(Visibility.Visible);
        c.Convert("a", typeof(Visibility), "b", Inv).Should().Be(Visibility.Collapsed);
    }

    // ── StringMatchVisibilityConverter (multi) ───────────────────────────
    [Fact]
    public void StringMatchVisibility_visible_when_both_strings_match()
    {
        var c = new StringMatchVisibilityConverter();
        c.Convert(new object[] { "p", "p" }, typeof(Visibility), null, Inv).Should().Be(Visibility.Visible);
        c.Convert(new object[] { "p", "q" }, typeof(Visibility), null, Inv).Should().Be(Visibility.Collapsed);
        c.Convert(new object[] { "p" }, typeof(Visibility), null, Inv).Should().Be(Visibility.Collapsed,
            "a non-pair never matches");
    }

    // ── WidthToColumnsConverter ──────────────────────────────────────────
    [Theory]
    [InlineData(360.0, 1)]
    [InlineData(719.0, 1)]
    [InlineData(720.0, 2)]
    [InlineData(1200.0, 2)]
    public void WidthToColumns_collapses_to_one_below_threshold(double width, int expected)
    {
        new WidthToColumnsConverter().Convert(width, typeof(int), null, Inv).Should().Be(expected);
    }

    // ── ZeroCountToVisibilityConverter ───────────────────────────────────
    [Fact]
    public void ZeroCountToVisibility_visible_only_when_empty()
    {
        var c = new ZeroCountToVisibilityConverter();
        c.Convert(0, typeof(Visibility), null, Inv).Should().Be(Visibility.Visible);
        c.Convert(3, typeof(Visibility), null, Inv).Should().Be(Visibility.Collapsed);
    }

    // ── Null(Not)ToVisibilityConverter ───────────────────────────────────
    [Fact]
    public void NotNullToVisibility_shows_when_set()
    {
        var c = new NotNullToVisibilityConverter();
        c.Convert(new object(), typeof(Visibility), null, Inv).Should().Be(Visibility.Visible);
        c.Convert(null!, typeof(Visibility), null, Inv).Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void NullToVisibility_shows_when_null()
    {
        var c = new NullToVisibilityConverter();
        c.Convert(null!, typeof(Visibility), null, Inv).Should().Be(Visibility.Visible);
        c.Convert(new object(), typeof(Visibility), null, Inv).Should().Be(Visibility.Collapsed);
    }
}
