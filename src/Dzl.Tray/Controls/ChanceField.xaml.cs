using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Dzl.Tray.Controls;

/// <summary>A compact 0..1 (configurable) numeric field: the button shows the value, a click opens a popup
/// with a slider plus a <see cref="Wpf.Ui.Controls.NumberBox"/> for precise entry. The value is clamped to
/// [<see cref="Minimum"/>, <see cref="Maximum"/>]. <see cref="ValueCommitted"/> fires once when the popup
/// closes (or Enter is pressed) — grid cells persist on that, not on every live <see cref="Value"/> change.</summary>
public partial class ChanceField : UserControl
{
    public ChanceField()
    {
        InitializeComponent();
        UpdateDisplay();
        Pop.CustomPopupPlacementCallback = PlaceCenteredBelow;
        DataObject.AddPastingHandler(Num, OnEntryPaste); // sanitize pasted text to numeric
        Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(this, OnOutsidePress);
        LostMouseCapture += OnLostCapture;
    }

    /// <summary>Center the popup horizontally under the button (4px gap below it).</summary>
    private static CustomPopupPlacement[] PlaceCenteredBelow(Size popupSize, Size targetSize, Point offset) =>
        new[]
        {
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + 4),
                PopupPrimaryAxis.Horizontal),
        };

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ChanceField),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnVisualAffectingChanged, CoerceValue));

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
        nameof(Decimals), typeof(int), typeof(ChanceField), new PropertyMetadata(2, OnVisualAffectingChanged));

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(ChanceField), new PropertyMetadata("0"));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    /// <summary>The button caption: <see cref="Value"/> formatted to <see cref="Decimals"/> places (read-only).</summary>
    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextKey, value);
    }

    /// <summary>Raised once when the value is committed: the popup closed, or Enter was pressed in the box.</summary>
    public event EventHandler? ValueCommitted;

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var c = (ChanceField)d;
        var clamped = Math.Clamp((double)baseValue, c.Minimum, c.Maximum);
        // Round to Decimals so slider float noise (e.g. 0.35000000000000003) never reaches the model/file.
        return Math.Round(clamped, Math.Max(0, c.Decimals), MidpointRounding.AwayFromZero);
    }

    private static void OnVisualAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ChanceField)d;
        if (e.Property == DecimalsProperty) { c.CoerceValue(ValueProperty); return; } // re-round; its own change re-runs this
        c.UpdateDisplay();
        c.SyncEntryFromValue(); // keep the popup entry in step with the slider (skipped while the user is typing)
        // The slider / entry live in the popup's own namescope and edit Value via ElementName / TextChanged;
        // that write does NOT reliably flow back to the bound source (item.Chance, EditChanceValue, …). Force
        // it so every consumer — grid cells AND standalone fields read on Apply/Add — sees the current value.
        BindingOperations.GetBindingExpression(c, ValueProperty)?.UpdateSource();
    }

    // Live two-way between the entry text and Value, without a binding (a converter binding rewrites the box
    // mid-keystroke and eats the decimal point). _syncing breaks the echo: typing sets Value (moving the
    // slider) without rewriting the box; a slider/external Value change rewrites the box but doesn't re-parse.
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

    private void UpdateDisplay()
    {
        var format = Decimals > 0 ? "0." + new string('#', Decimals) : "0";
        DisplayText = Value.ToString(format, CultureInfo.InvariantCulture); // dot, matching the CE files
    }

    // Numeric-only entry: allow digits and a single decimal separator ('.' or ',').
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

    // On focus loss, re-display the canonical value: clamp (min/max), Decimals rounding, comma→dot.
    private void OnEntryLostFocus(object sender, RoutedEventArgs e) => SyncEntryFromValue();

    // A Popup hosting editable controls inside a DataGrid can't use StaysOpen (the grid steals the mouse) and
    // its clicks otherwise bubble to the grid, which selects the row and grabs focus — so the entry was dead
    // to clicks/arrows. Use the ComboBox pattern: while open, hold a SubTree mouse capture on this control.
    // SubTree capture routes presses inside our subtree (the popup) to the slider/entry (and never to the
    // grid), while a press outside raises PreviewMouseDownOutsideCapturedElement so we can close.
    private bool _wasOpen;
    private double _openValue;

    private void OnTogglePreviewDown(object sender, MouseButtonEventArgs e) => _wasOpen = Pop.IsOpen;

    private void OnToggleClick(object sender, MouseButtonEventArgs e)
    {
        if (_wasOpen) ClosePopup();   // clicking the face while open → close
        else OpenPopup();             // was closed → open
    }

    private void OpenPopup()
    {
        _openValue = Value; // baseline so closing only commits when the value actually changed
        SyncEntryFromValue(); // seed the entry from the current value
        Pop.IsOpen = true;
        // After the popup renders: capture the mouse to our subtree (so its clicks reach the slider/entry and
        // never leak to the DataGrid) and focus the entry (so typing/arrows land there, not on the row).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Mouse.Capture(this, CaptureMode.SubTree);
            Num.Focus();
            Num.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ClosePopup()
    {
        if (!Pop.IsOpen) return;
        Pop.IsOpen = false; // set first so the re-entrant LostMouseCapture sees it already closed
        if (ReferenceEquals(Mouse.Captured, this)) Mouse.Capture(null);
    }

    // A press outside our captured subtree (another row, empty space, another field) → close.
    private void OnOutsidePress(object sender, MouseButtonEventArgs e) => ClosePopup();

    // A child (e.g. the slider thumb on press) can take capture from us. Reclaim it with SubTree so the drag
    // keeps working (SubTree still delivers MouseMove to the child) AND we keep outside-click detection.
    // If capture went somewhere outside our subtree, close instead.
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!Pop.IsOpen || ReferenceEquals(Mouse.Captured, this)) return;
        if (Mouse.Captured is null || IsInOurSubtree(Mouse.Captured as DependencyObject))
            Mouse.Capture(this, CaptureMode.SubTree);
        else
            ClosePopup();
    }

    private bool IsInOurSubtree(DependencyObject? node)
    {
        for (; node is not null;
             node = node is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, this) || ReferenceEquals(node, Pop.Child)) return true;
        }
        return false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (Math.Abs(Value - _openValue) > 1e-9) ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void OnNumKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: ClosePopup(); e.Handled = true; break; // closing fires ValueCommitted
            // Up/Down step by SmallChange like the slider (coercion clamps + rounds). Left/Right stay free
            // for caret movement within the number.
            case Key.Up: Value += SmallChange; e.Handled = true; break;
            case Key.Down: Value -= SmallChange; e.Handled = true; break;
        }
    }
}
