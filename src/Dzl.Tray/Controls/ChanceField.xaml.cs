using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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
        nameof(Decimals), typeof(int), typeof(ChanceField), new PropertyMetadata(3, OnVisualAffectingChanged));

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(ChanceField), new PropertyMetadata(false));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
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
        return Math.Clamp((double)baseValue, c.Minimum, c.Maximum);
    }

    private static void OnVisualAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ChanceField)d).UpdateDisplay();

    private void UpdateDisplay()
    {
        var format = Decimals > 0 ? "0." + new string('#', Decimals) : "0";
        DisplayText = Value.ToString(format, CultureInfo.CurrentCulture);
    }

    private void OnPopupClosed(object? sender, EventArgs e) => ValueCommitted?.Invoke(this, EventArgs.Empty);

    private void OnNumKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { IsOpen = false; e.Handled = true; } // closing the popup fires ValueCommitted
    }
}
