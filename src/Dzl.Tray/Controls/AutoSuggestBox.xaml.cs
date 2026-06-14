using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>
/// Reusable autocomplete box (the "React component" for every "search a classname / preset" field): bind a
/// <see cref="Suggestions"/> pool and a two-way <see cref="Text"/>, set a <see cref="Placeholder"/>, and
/// handle <see cref="Submitted"/> (Enter) to add/commit. The dropdown opens and filters as you type; picking a
/// suggestion commits it without re-filtering; free text is allowed. Filtering logic lives in
/// <see cref="AutoSuggest.Filter"/> (unit-tested once).
/// </summary>
public partial class AutoSuggestBox : UserControl
{
    private readonly ObservableCollection<string> _filtered = new();
    private bool _suspend;

    public AutoSuggestBox()
    {
        InitializeComponent();
        Combo.ItemsSource = _filtered;
    }

    /// <summary>The full pool of candidate strings to filter (e.g. all classnames). Read live on each edit.</summary>
    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(AutoSuggestBox), new PropertyMetadata(null));

    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>The typed / selected value (two-way). Hosts bind this to their VM field.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(AutoSuggestBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Hint shown while the box is empty (an editable ComboBox has no native placeholder).</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(AutoSuggestBox), new PropertyMetadata(""));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Max suggestions shown in the dropdown (keeps a huge pool responsive). Default 50.</summary>
    public static readonly DependencyProperty MaxSuggestionsProperty = DependencyProperty.Register(
        nameof(MaxSuggestions), typeof(int), typeof(AutoSuggestBox), new PropertyMetadata(50));

    public int MaxSuggestions
    {
        get => (int)GetValue(MaxSuggestionsProperty);
        set => SetValue(MaxSuggestionsProperty, value);
    }

    /// <summary>Raised when the user presses Enter — the host adds/commits the current <see cref="Text"/>.</summary>
    public event EventHandler? Submitted;

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (AutoSuggestBox)d;
        if (!box._suspend) box.Refilter();
    }

    private void Refilter()
    {
        _filtered.Clear();
        var pool = Suggestions?.OfType<string>() ?? Enumerable.Empty<string>();
        foreach (var s in AutoSuggest.Filter(pool, Text, MaxSuggestions)) _filtered.Add(s);
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Combo.IsDropDownOpen = false;
            Submitted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Escape or Key.Up or Key.Down) return;
        Combo.IsDropDownOpen = _filtered.Count > 0;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Combo.SelectedItem is not string pick) return;
        // Commit the pick without re-filtering, so the Clear() in Refilter can't drop the selection.
        _suspend = true;
        Text = pick;
        _suspend = false;
    }
}
