using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>A chance editor over one clamped <see cref="Value"/> in [<see cref="Minimum"/>, <see cref="Maximum"/>],
/// rounded to <see cref="Decimals"/>. Two layouts share the same logic:
/// <list type="bullet">
/// <item>Popup (default) — a compact button that opens a slider+entry flyout; for standalone use.</item>
/// <item>Inline (<see cref="Inline"/>=true) — slider+entry shown in place; for DataGrid cells, where a popup
/// loses keyboard focus to the grid.</item>
/// </list>
/// In inline mode <see cref="ValueCommitted"/> fires once when editing ends (focus leaves) and the value
/// changed — the persist hook for grid cells. Standalone (popup) callers read <see cref="Value"/> on Apply/Add.</summary>
public partial class ChanceField : UserControl
{
    public ChanceField()
    {
        InitializeComponent();
        SyncEntryFromValue();
        Pop.CustomPopupPlacementCallback = PlaceCenteredBelow;
        DataObject.AddPastingHandler(InlineEntry, OnEntryPaste);
        DataObject.AddPastingHandler(PopupEntry, OnEntryPaste);
        IsKeyboardFocusWithinChanged += OnFocusWithinChanged;
        _commitTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _commitTimer.Tick += (_, _) => FlushCommit();
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

    /// <summary>True = slider+entry shown in place (DataGrid cells); false = compact button + popup (default).</summary>
    public static readonly DependencyProperty InlineProperty = DependencyProperty.Register(
        nameof(Inline), typeof(bool), typeof(ChanceField), new PropertyMetadata(false));

    public bool Inline
    {
        get => (bool)GetValue(InlineProperty);
        set => SetValue(InlineProperty, value);
    }

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(ChanceField), new PropertyMetadata("0"));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    /// <summary>The popup button caption: <see cref="Value"/> formatted to <see cref="Decimals"/> places.</summary>
    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextKey, value);
    }

    /// <summary>Raised once when inline editing ends (focus leaves) and the value changed.</summary>
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
        c.DisplayText = c.Value.ToString(c.Decimals > 0 ? "0." + new string('#', c.Decimals) : "0", CultureInfo.InvariantCulture);
        c.SyncEntryFromValue();
        // The slider edits Value via ElementName; force the outer binding (item.Chance, EditChanceValue, …) to
        // pick it up so every consumer sees the current value.
        BindingOperations.GetBindingExpression(c, ValueProperty)?.UpdateSource();
        // Inline (grid) mode: auto-commit shortly after the last change, so edits persist without clicking
        // away. ONLY while the user is actually editing this field (focus within) — a programmatic Value
        // change (the initial TwoWay binding when the items grid (re)builds its rows on preset select/rename)
        // must NOT arm the commit timer, or every row would auto-persist ~350ms later as a phantom edit.
        if (c.Inline && c.IsKeyboardFocusWithin) { c._commitTimer.Stop(); c._commitTimer.Start(); }
    }

    // Live two-way between the entry text and Value without a binding (a converter binding rewrites the box
    // mid-keystroke and eats the decimal point). _syncing breaks the echo: typing sets Value (moving the
    // slider) without rewriting the box; a slider/external change rewrites the box(es) but doesn't re-parse.
    private bool _syncing;

    private void SyncEntryFromValue()
    {
        if (_syncing) return;
        _syncing = true;
        var t = Value.ToString("0.###", CultureInfo.InvariantCulture);
        if (InlineEntry is not null) InlineEntry.Text = t;
        if (PopupEntry is not null) PopupEntry.Text = t;
        _syncing = false;
    }

    private void OnEntryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || sender is not TextBox tb) return;
        var s = tb.Text.Replace(',', '.').Trim();
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

    // ===== Inline mode: debounced auto-commit (persists ~350ms after the last change) + flush on focus leave =====
    private readonly System.Windows.Threading.DispatcherTimer _commitTimer;
    private double _committedValue;

    private void FlushCommit()
    {
        _commitTimer.Stop();
        if (Math.Abs(Value - _committedValue) > 1e-9)
        {
            _committedValue = Value;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!Inline) return;                 // popup-mode callers read Value directly on Apply/Add
        if ((bool)e.NewValue) _committedValue = Value; // baseline when editing starts
        else FlushCommit();                  // commit immediately when focus leaves the cell
    }

    // ===== Popup mode: button toggles a StaysOpen=False flyout (standalone use, never in a DataGrid) =====
    private bool _wasOpen;
    private Window? _popupWindow;

    private void OnTogglePreviewDown(object sender, MouseButtonEventArgs e) => _wasOpen = Pop.IsOpen;

    private void OnToggleClick(object sender, MouseButtonEventArgs e)
    {
        if (_wasOpen) { Pop.IsOpen = false; return; } // clicking the face while open → close
        SyncEntryFromValue();
        Pop.IsOpen = true;
        // A Popup does not follow its window; close it if the window moves/resizes/deactivates so it can't be
        // left floating detached from the button.
        _popupWindow = Window.GetWindow(this);
        if (_popupWindow is not null)
        {
            _popupWindow.LocationChanged += OnHostShifted;
            _popupWindow.Deactivated += OnHostShifted;
            _popupWindow.SizeChanged += OnHostResized;
        }
    }

    private void OnHostShifted(object? sender, EventArgs e) => Pop.IsOpen = false;
    private void OnHostResized(object sender, SizeChangedEventArgs e) => Pop.IsOpen = false;

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (_popupWindow is null) return;
        _popupWindow.LocationChanged -= OnHostShifted;
        _popupWindow.Deactivated -= OnHostShifted;
        _popupWindow.SizeChanged -= OnHostResized;
        _popupWindow = null;
    }

    /// <summary>Center the popup horizontally under the button (4px gap below it).</summary>
    private static CustomPopupPlacement[] PlaceCenteredBelow(Size popupSize, Size targetSize, Point offset) =>
        new[]
        {
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + 4),
                PopupPrimaryAxis.Horizontal),
        };
}
