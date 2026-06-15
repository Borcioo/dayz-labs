using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>
/// Attached behaviour that restricts a <see cref="TextBox"/> to non-negative integer input: any keystroke or
/// paste that would introduce a non-digit character is rejected before it reaches the text. The bound VM
/// still validates on commit (so an empty box or out-of-range value is caught there) — this only stops the
/// obviously-wrong characters (letters, signs, separators) from ever being typed. Reusable across every CE
/// editor with integer fields; set <c>ctl:NumericBehavior.IntegerOnly="True"</c> on the box (or in a Style).
/// Works on <c>ui:TextBox</c> too, which derives from the WPF <see cref="TextBox"/>.
/// </summary>
public static class NumericBehavior
{
    public static readonly DependencyProperty IntegerOnlyProperty = DependencyProperty.RegisterAttached(
        "IntegerOnly", typeof(bool), typeof(NumericBehavior), new PropertyMetadata(false, OnIntegerOnlyChanged));

    public static bool GetIntegerOnly(DependencyObject d) => (bool)d.GetValue(IntegerOnlyProperty);
    public static void SetIntegerOnly(DependencyObject d, bool value) => d.SetValue(IntegerOnlyProperty, value);

    /// <summary>True when <paramref name="text"/> is a non-empty run of decimal digits — the rule both the
    /// keystroke and paste filters apply. Pure + public so it can be unit-tested without a WPF event.</summary>
    public static bool IsDigits(string? text) => !string.IsNullOrEmpty(text) && text.All(char.IsDigit);

    private static void OnIntegerOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(tb, OnPaste);
        }
        else
        {
            tb.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(tb, OnPaste);
        }
    }

    // Block the keystroke when the inserted text isn't all digits (lets backspace/delete/arrows through —
    // those don't raise PreviewTextInput).
    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsDigits(e.Text);

    // Block a paste whose payload isn't all digits.
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is string s && IsDigits(s)) return;
        e.CancelCommand();
    }

    // ---- FloatOnly: non-negative decimal (digits + at most one '.') ----
    // Integer fields use IntegerOnly; fields that are genuinely fractional (chances entered as text,
    // simulation values, world coordinates, spawn params) need to accept a decimal point, which
    // IntegerOnly would wrongly block. The guard validates the WOULD-BE text (current ± edit) so a
    // second '.' is rejected. The bound VM still does the authoritative parse/clamp on commit.

    public static readonly DependencyProperty FloatOnlyProperty = DependencyProperty.RegisterAttached(
        "FloatOnly", typeof(bool), typeof(NumericBehavior), new PropertyMetadata(false, OnFloatOnlyChanged));

    public static bool GetFloatOnly(DependencyObject d) => (bool)d.GetValue(FloatOnlyProperty);
    public static void SetFloatOnly(DependencyObject d, bool value) => d.SetValue(FloatOnlyProperty, value);

    private static readonly Regex FloatCandidate = new(@"^\d*\.?\d*$", RegexOptions.Compiled);

    /// <summary>True if <paramref name="text"/> is a valid partial non-negative decimal (empty, "12",
    /// "12.", ".5", "1.5"). Pure + public for unit testing.</summary>
    public static bool IsFloatCandidate(string? text) =>
        string.IsNullOrEmpty(text) || FloatCandidate.IsMatch(text);

    private static void OnFloatOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.PreviewTextInput += OnFloatPreview;
            DataObject.AddPastingHandler(tb, OnFloatPaste);
        }
        else
        {
            tb.PreviewTextInput -= OnFloatPreview;
            DataObject.RemovePastingHandler(tb, OnFloatPaste);
        }
    }

    private static void OnFloatPreview(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox tb) e.Handled = !IsFloatCandidate(Proposed(tb, e.Text));
    }

    private static void OnFloatPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is TextBox tb && e.DataObject.GetData(typeof(string)) is string s &&
            IsFloatCandidate(Proposed(tb, s))) return;
        e.CancelCommand();
    }

    // The text the box would hold if `insert` replaced the current selection.
    private static string Proposed(TextBox tb, string insert)
    {
        var t = tb.Text ?? "";
        var start = tb.SelectionStart;
        var len = tb.SelectionLength;
        return t.Substring(0, start) + insert + t.Substring(start + len);
    }
}
