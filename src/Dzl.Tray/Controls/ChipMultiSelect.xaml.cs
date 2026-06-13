using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>Reusable chip editor for a list-of-strings field (Usage / Value / Tag).
/// Shows the bound <see cref="Items"/> collection as removable chips (label + ×) and offers an
/// editable typeahead combo (suggestions from <see cref="Suggestions"/>) that also accepts free text
/// (Enter adds a value not in the list). Adding/removing mutates the bound <see cref="Items"/> collection
/// directly, so the owning row's <c>ToEntry()</c> round-trips edits with no extra wiring.
///
/// Designed to live both in a (compact) grid cell and the (expanded) detail panel — it simply wraps its
/// chips. <see cref="Items"/> is two-way and expects the actual <see cref="ObservableCollection{T}"/> from
/// the row VM; <see cref="Suggestions"/> is the valid-value set from cfglimitsdefinition.</summary>
public partial class ChipMultiSelect : UserControl
{
    public ChipMultiSelect()
    {
        InitializeComponent();
    }

    /// <summary>The bound value collection (the row VM's actual ObservableCollection&lt;string&gt;).
    /// Mutated in place when chips are added/removed.</summary>
    // OneWay by default: the control NEVER reassigns Items, it only mutates the bound collection's
    // contents (Items.Add/RemoveAt). TwoWay would force WPF to write the property back and crash when
    // bound to a get-only ObservableCollection (e.g. TypeRowVm.Usage, ComboVm.Members).
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IList), typeof(ChipMultiSelect),
        new FrameworkPropertyMetadata(null, OnItemsChanged));

    public IList? Items
    {
        get => (IList?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>Valid values offered in the add-combo's dropdown (free text still allowed).</summary>
    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IEnumerable), typeof(ChipMultiSelect),
        new PropertyMetadata(null));

    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>Placeholder text for the add affordance.</summary>
    public static readonly DependencyProperty AddHintProperty = DependencyProperty.Register(
        nameof(AddHint), typeof(string), typeof(ChipMultiSelect),
        new PropertyMetadata("add…"));

    public string AddHint
    {
        get => (string)GetValue(AddHintProperty);
        set => SetValue(AddHintProperty, value);
    }

    /// <summary>Identifies which dictionary kind this chip control edits ("usage"/"value"/"tag"/"category").
    /// Used by the host to offer registering a free-added value into the live dictionary. Empty = no kind.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(ChipMultiSelect), new PropertyMetadata(""));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The value most recently ADDED via the add affordance (null after a remove). Lets the host
    /// detect a just-added free value and offer to register it in the dictionary.</summary>
    public string? LastAdded { get; private set; }

    /// <summary>The display list the ItemsControl binds to. We keep a private mirror so the chips re-render
    /// even when the bound <see cref="Items"/> is a plain IList (no change notification) — though in practice
    /// it is an ObservableCollection. Re-synced on every mutation + when Items changes.</summary>
    public ObservableCollection<string> Chips { get; } = new();

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ChipMultiSelect)d;
        if (e.OldValue is INotifyCollectionChanged oldNcc)
            oldNcc.CollectionChanged -= c.OnSourceCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newNcc)
            newNcc.CollectionChanged += c.OnSourceCollectionChanged;
        c.SyncChips();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncChips();

    private void SyncChips()
    {
        Chips.Clear();
        if (Items is null) return;
        foreach (var o in Items)
            if (o is string s) Chips.Add(s);
    }

    private void OnRemoveChip(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string val } && Items is not null)
        {
            // Remove the first matching value (case-sensitive, mirrors XML semantics).
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is string s && s == val)
                {
                    RaiseBeforeChange();
                    Items.RemoveAt(i);
                    LastAdded = null;   // a remove is not an "add" — clear the free-add affordance
                    break;
                }
            }
            RaiseChanged();
        }
    }

    private void OnAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ComboBox cb)
        {
            CommitAdd(cb);
            e.Handled = true;
        }
    }

    private void OnAddSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a suggestion from the dropdown commits immediately.
        if (sender is ComboBox { SelectedItem: string s } cb && !string.IsNullOrWhiteSpace(s))
        {
            AddValue(s);
            cb.SelectedItem = null;
            cb.Text = "";
        }
    }

    private void CommitAdd(ComboBox cb)
    {
        var text = (cb.Text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return;
        AddValue(text);
        cb.SelectedItem = null;
        cb.Text = "";
    }

    private void AddValue(string val)
    {
        val = val.Trim();
        if (string.IsNullOrEmpty(val) || Items is null) return;
        // No duplicates (case-insensitive) — matches what the engine would dedupe anyway.
        foreach (var o in Items)
            if (o is string s && string.Equals(s, val, System.StringComparison.OrdinalIgnoreCase)) return;
        RaiseBeforeChange();
        Items.Add(val);
        LastAdded = val;
        RaiseChanged();
    }

    /// <summary>Raised immediately BEFORE the bound collection is mutated (add or remove), so the host
    /// can snapshot the pre-change state for undo.</summary>
    public event System.EventHandler? BeforeChange;

    private void RaiseBeforeChange() => BeforeChange?.Invoke(this, System.EventArgs.Empty);

    /// <summary>Raised after the bound collection is mutated, so the host (detail panel / VM) can re-lint
    /// and refresh the grid's text proxy. Carries the changed <see cref="Items"/> as a courtesy.</summary>
    public event System.EventHandler? Changed;

    private void RaiseChanged() => Changed?.Invoke(this, System.EventArgs.Empty);
}
