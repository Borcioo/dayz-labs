using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
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
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IList), typeof(ChipMultiSelect),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnItemsChanged));

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
                if (Items[i] is string s && s == val) { Items.RemoveAt(i); break; }
            }
            SyncChips();
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
        Items.Add(val);
        SyncChips();
        RaiseChanged();
    }

    /// <summary>Raised after the bound collection is mutated, so the host (detail panel / VM) can re-lint
    /// and refresh the grid's text proxy. Carries the changed <see cref="Items"/> as a courtesy.</summary>
    public event System.EventHandler? Changed;

    private void RaiseChanged() => Changed?.Invoke(this, System.EventArgs.Empty);
}
