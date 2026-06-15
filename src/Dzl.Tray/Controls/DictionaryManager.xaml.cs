using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The CE Dictionaries manager: four editable base lists (cfglimitsdefinition.xml) plus a
/// named-combos section (cfglimitsdefinitionuser.xml). DataContext = <see cref="DictionaryManagerVm"/>.
/// The control is purely presentational — Add/Remove/Rename clicks forward to the bound
/// <see cref="DictionaryListVm"/>/<see cref="DictionaryManagerVm"/>, which calls the DictionaryService and
/// then refreshes the Types editor.</summary>
public partial class DictionaryManager : UserControl
{
    /// <summary>Converts the new-combo kind ComboBox's "usage"/"value" tag ↔ the VM's bool (true = usage).</summary>
    public static readonly IValueConverter UsageBoolConv = new UsageBoolConverter();
    /// <summary>Visible when the bound value is null (the "select a combo" hint).</summary>
    public static readonly IValueConverter NullToVisConv = new NullToVisibilityConverter();

    public DictionaryManager()
    {
        InitializeComponent();
    }

    private DictionaryManagerVm? Vm => DataContext as DictionaryManagerVm;

    private static DictionaryListVm? ListOf(object sender)
        => (sender as FrameworkElement)?.Tag as DictionaryListVm;

    private void OnAddClick(object sender, RoutedEventArgs e) => ListOf(sender)?.RequestAdd();

    private void OnAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement fe && fe.Tag is DictionaryListVm list)
        {
            list.RequestAdd();
            e.Handled = true;
        }
    }

    // Per-row remove: the button's DataContext is the entry; the active list is the VM's SelectedList.
    private void OnRowRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DictEntryVm entry } && Vm?.SelectedList is { } list)
            list.RequestRemove(entry.Name);
    }

    // Inline rename (pencil click or double-click). Unified for base-dictionary entries (DictEntryVm) and
    // named combos (ComboVm) so both lists share the same edit affordances.
    private void OnEntryEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || Vm is not { } vm) return;
        if (fe.DataContext is DictEntryVm entry && vm.SelectedList is { } list) list.BeginEdit(entry);
        else if (fe.DataContext is ComboVm combo) vm.BeginEditCombo(combo);
    }

    // Focus + select-all the edit box the moment it appears.
    private void OnEditBoxVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is Wpf.Ui.Controls.TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void OnEditBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitEntry(sender); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEntry(sender); e.Handled = true; }
    }

    private void OnEditBoxCommit(object sender, RoutedEventArgs e) => CommitEntry(sender);

    // Cancel via PreviewMouseDown so it runs BEFORE the edit box loses focus — CancelEdit flips IsEditing off,
    // so the box's LostFocus commit then no-ops (CommitEdit guards on IsEditing).
    private void OnEditCancel(object sender, MouseButtonEventArgs e) => CancelEntry(sender);

    private void CommitEntry(object sender)
    {
        if (sender is not FrameworkElement fe || Vm is not { } vm) return;
        if (fe.DataContext is DictEntryVm entry && vm.SelectedList is { } list) list.CommitEdit(entry);
        else if (fe.DataContext is ComboVm combo) vm.CommitComboEdit(combo);
    }

    private void CancelEntry(object sender)
    {
        if (sender is not FrameworkElement fe || Vm is not { } vm) return;
        if (fe.DataContext is DictEntryVm entry && vm.SelectedList is { } list) list.CancelEdit(entry);
        else if (fe.DataContext is ComboVm combo) vm.CancelComboEdit(combo);
    }

    private void OnAddComboClick(object sender, RoutedEventArgs e) => Vm?.AddCombo();
    private void OnComboMembersChanged(object? sender, EventArgs e) => Vm?.SaveSelectedComboMembers();

    // Per-row combo remove: select that combo, then reuse the confirm+remove flow.
    private void OnRemoveComboRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ComboVm combo } && Vm is { } vm)
        {
            vm.SelectedCombo = combo;
            vm.RemoveSelectedCombo();
        }
    }

    private sealed class UsageBoolConverter : IValueConverter
    {
        // VM bool → tag string for the ComboBox SelectedValue.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b ? "value" : "usage";

        // Tag string → VM bool.
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && string.Equals(s, "usage", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
