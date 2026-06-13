using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The CE Random Presets editor (Economy "Random Presets" tab). Presentational only — clicks and
/// inline-edit commits forward to the bound <see cref="RandomPresetsVm"/>, which calls
/// <see cref="Dzl.Core.App.RandomPresetsService"/> and snapshots/writes each edit. DataContext = a
/// <see cref="RandomPresetsVm"/>.</summary>
public partial class RandomPresetsEditor : UserControl
{
    public RandomPresetsEditor()
    {
        InitializeComponent();
    }

    private RandomPresetsVm? Vm => DataContext as RandomPresetsVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnNewKindChanged(object sender, SelectionChangedEventArgs e)
    {
        // Index 0 = cargo, 1 = attachments.
        if (Vm is { } vm && sender is ComboBox cb) vm.NewPresetIsCargo = cb.SelectedIndex == 0;
    }

    private void OnAddPresetClick(object sender, RoutedEventArgs e) => Vm?.AddPreset();

    private void OnPresetCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not PresetRowVm row || Vm is not { } vm) return;
        // Only the Chance column is editable; persist after the binding writes back.
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (ReferenceEquals(vm.SelectedPreset, row)) vm.SaveSelectedPresetChance();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnRemovePresetClick(object sender, RoutedEventArgs e) => Vm?.RemoveSelectedPreset();

    private void OnRenamePresetClick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.SelectedPreset is not { } row) { if (Vm is { } v) v.Status = "✗ select a preset to rename"; return; }
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var next = PromptDialog.Show(owner, $"Rename {row.KindLabel} preset", $"Rename \"{row.Name}\" to:", row.Name);
        if (string.IsNullOrWhiteSpace(next)) return;
        vm.RenameSelectedPreset(next.Trim());
    }

    // Per-row quick actions: select that row, then reuse the rename/remove flow.
    private void OnRowRenamePresetClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: PresetRowVm row })
        {
            vm.SelectedPreset = row;
            OnRenamePresetClick(sender, e);
        }
    }

    private void OnRowRemovePresetClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: PresetRowVm row })
        {
            vm.SelectedPreset = row;
            vm.RemoveSelectedPreset();
        }
    }

    private void OnRowToggleDisabledClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: PresetRowVm row })
            vm.ToggleDisabled(row);
    }

    private void OnAddItemClick(object sender, RoutedEventArgs e) => Vm?.AddItem();

    private void OnAddItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddItem(); e.Handled = true; }
    }

    // Add-item classname combo: open the suggestion dropdown as the user types; Enter adds the item.
    private void OnItemComboKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (e.Key == Key.Enter)
        {
            cb.IsDropDownOpen = false;
            Vm?.AddItem();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Escape or Key.Up or Key.Down) return;
        cb.IsDropDownOpen = Vm is { ItemSuggestions.Count: > 0 };
    }

    // A dropdown pick (mouse or arrow-highlight): commit the name without re-filtering, so the Clear()
    // inside the suggestion refresh can't drop the selection and blank the box.
    private void OnItemComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not { } vm || sender is not ComboBox cb) return;
        if (cb.SelectedItem is not string pick) return;
        vm.SuspendSuggestions = true;
        vm.NewItemName = pick;
        vm.SuspendSuggestions = false;
    }

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItemVm item) Vm?.RemoveItem(item);
    }

    private void OnItemCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not PresetItemVm item) return;
        // The binding commits on LostFocus; defer so the edited value is written back first.
        Dispatcher.BeginInvoke(new System.Action(item.Commit),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
