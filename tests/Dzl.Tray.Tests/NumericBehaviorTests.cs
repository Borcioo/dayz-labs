using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="NumericBehavior"/> — the integer-only input filter shared by the CE editors'
/// numeric fields. Only the pure rule (<see cref="NumericBehavior.IsDigits"/>) is asserted here; the keystroke
/// and paste wiring is WPF event plumbing exercised by the realization smoke test.</summary>
public class NumericBehaviorTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("42", true)]
    [InlineData("007", true)]
    [InlineData("", false)]      // empty → rejected (the VM treats an empty box on commit)
    [InlineData("abc", false)]
    [InlineData("12a", false)]
    [InlineData("1.5", false)]   // no decimal point
    [InlineData("-3", false)]    // no sign (fields are non-negative)
    [InlineData("3 ", false)]    // no whitespace
    public void IsDigits_accepts_only_nonempty_runs_of_digits(string input, bool expected) =>
        NumericBehavior.IsDigits(input).Should().Be(expected);

    [Theory]
    [InlineData("", true)]       // empty is a valid partial entry
    [InlineData("0", true)]
    [InlineData("12", true)]
    [InlineData("1.5", true)]
    [InlineData(".5", true)]
    [InlineData("12.", true)]    // mid-typing a decimal
    [InlineData("1.5.2", false)] // only one decimal point
    [InlineData("abc", false)]
    [InlineData("1a", false)]
    [InlineData("-1", false)]    // non-negative only
    [InlineData("1 ", false)]
    public void IsFloatCandidate_accepts_partial_nonnegative_decimals(string input, bool expected) =>
        NumericBehavior.IsFloatCandidate(input).Should().Be(expected);
}
