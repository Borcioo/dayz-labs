using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>A compact inline chance editor: a slider plus a numeric entry, bound to one clamped
/// <see cref="Value"/> in [<see cref="Minimum"/>, <see cref="Maximum"/>] and rounded to <see cref="Decimals"/>.
/// Rendered inline (no popup) so it works inside a DataGrid cell, where a popup would lose keyboard focus to
/// the grid. <see cref="ValueCommitted"/> fires once when editing ends (focus leaves) and the value changed —
/// grid cells persist on that, not on every live keystroke/drag.</summary>
public partial class ChanceField : UserControl
{
    public ChanceField()
    {
        InitializeComponent();
        SyncEntryFromValue();
        DataObject.AddPastingHandler(Num, OnEntryPaste);   // keep pasted text numeric
        IsKeyboardFocusWithinChanged += OnFocusWithinChanged;
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ChanceField),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged, CoerceValue));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ChanceField), new PropertyMetadata(0.0));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ChanceField), new PropertyMetadata(1.0));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(ChanceField), new PropertyMetadata(0.05));

    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(ChanceField), new PropertyMetadata(2, OnValueChanged));

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    /// <summary>Raised once when editing ends (focus leaves the control) and the value changed — the persist
    /// hook for DataGrid cells. Standalone fields ignore it and read <see cref="Value"/> on Apply/Add.</summary>
    public event EventHandler? ValueCommitted;

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var c = (ChanceField)d;
        var clamped = Math.Clamp((double)baseValue, c.Minimum, c.Maximum);
        // Round to Decimals so slider float noise (e.g. 0.35000000000000003) never reaches the model/file.
        return Math.Round(clamped, Math.Max(0, c.Decimals), MidpointRounding.AwayFromZero);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ChanceField)d;
        if (e.Property == DecimalsProperty) { c.CoerceValue(ValueProperty); return; } // re-round on Decimals change
        c.SyncEntryFromValue();
        // The slider edits Value via ElementName; force the outer binding (item.Chance, EditChanceValue, …) to
        // pick it up so every consumer sees the current value without waiting for focus loss.
        BindingOperations.GetBindingExpression(c, ValueProperty)?.UpdateSource();
    }

    // Live two-way between the entry text and Value without a binding (a converter binding rewrites the box
    // mid-keystroke and eats the decimal point). _syncing breaks the echo: typing sets Value (moving the
    // slider) without rewriting the box; a slider/external change rewrites the box but doesn't re-parse.
    private bool _syncing;

    private void SyncEntryFromValue()
    {
        if (_syncing || Num is null) return;
        _syncing = true;
        Num.Text = Value.ToString("0.###", CultureInfo.InvariantCulture);
        _syncing = false;
    }

    private void OnEntryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        var s = Num.Text.Replace(',', '.').Trim();
        if (s.Length == 0) return;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            _syncing = true;
            Value = d; // coercion clamps + rounds; moves the slider; pushes to the bound source
            _syncing = false;
        }
    }

    // Numeric-only entry: digits plus a single decimal separator ('.' or ',').
    private void OnEntryPreviewInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox tb) e.Handled = !IsNumericInput(tb, e.Text);
    }

    private static bool IsNumericInput(TextBox tb, string text)
    {
        foreach (var ch in text)
        {
            if (char.IsDigit(ch)) continue;
            var isSeparator = ch is '.' or ',';
            var separatorPresent = tb.Text.Contains('.') || tb.Text.Contains(',');
            var selectionEatsSeparator = tb.SelectedText.Contains('.') || tb.SelectedText.Contains(',');
            if (isSeparator && (!separatorPresent || selectionEatsSeparator)) continue;
            return false;
        }
        return true;
    }

    private void OnEntryPaste(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.DataObject.GetDataPresent(DataFormats.Text) ? (string)e.DataObject.GetData(DataFormats.Text) : "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"^[0-9]*[.,]?[0-9]*$")) e.CancelCommand();
    }

    // Re-display the canonical value on focus loss (clamp, Decimals rounding, comma→dot).
    private void OnEntryLostFocus(object sender, RoutedEventArgs e) => SyncEntryFromValue();

    // Up/Down step by SmallChange like the slider (coercion clamps + rounds); Left/Right stay for the caret.
    private void OnEntryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Value += SmallChange; e.Handled = true; break;
            case Key.Down: Value -= SmallChange; e.Handled = true; break;
        }
    }

    // Commit once when editing ends: focus enters → snapshot baseline; focus leaves → fire if it changed.
    private double _focusValue;

    private void OnFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            _focusValue = Value;
        else if (Math.Abs(Value - _focusValue) > 1e-9)
            ValueCommitted?.Invoke(this, EventArgs.Empty);
    }
}
