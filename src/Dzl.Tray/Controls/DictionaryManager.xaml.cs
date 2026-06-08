using System;
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

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ListOf(sender) is { } list)
        {
            if (string.IsNullOrEmpty(list.Selected)) { if (Vm is { } vm) vm.Status = "✗ select an entry to remove"; return; }
            list.RequestRemove(list.Selected);
        }
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ListOf(sender) is not { } list) return;
        var current = list.Selected;
        if (string.IsNullOrEmpty(current)) { if (Vm is { } vm) vm.Status = "✗ select an entry to rename"; return; }
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var next = PromptDialog.Show(owner, $"Rename {list.Kind}", $"Rename \"{current}\" to:", current);
        if (string.IsNullOrWhiteSpace(next)) return;
        list.RequestRename(current, next.Trim());
    }

    private void OnAddComboClick(object sender, RoutedEventArgs e) => Vm?.AddCombo();
    private void OnRemoveComboClick(object sender, RoutedEventArgs e) => Vm?.RemoveSelectedCombo();
    private void OnComboMembersChanged(object? sender, EventArgs e) => Vm?.SaveSelectedComboMembers();

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
