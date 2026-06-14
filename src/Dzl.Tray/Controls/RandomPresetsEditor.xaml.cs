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

    // Per-row delete: select that row, then reuse the remove flow (rename is done in the Edit card).
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

    // Validation-finding click: select the offending preset in the master list and scroll it into view.
    private void OnFindingClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: CeFindingRow f }) return;
        vm.SelectByEntry(f.Entry);
        if (vm.SelectedPreset is { } sel) PresetGrid.ScrollIntoView(sel);
    }

    private void OnApplyEditsClick(object sender, RoutedEventArgs e) => Vm?.ApplyEdits();

    private void OnEditNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.ApplyEdits(); e.Handled = true; }
    }

    private void OnAddItemClick(object sender, RoutedEventArgs e) => Vm?.AddItem();

    // The reusable AutoSuggestBox raised Submitted (Enter) — add the item.
    private void OnItemSubmitted(object? sender, System.EventArgs e) => Vm?.AddItem();

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItemVm item) Vm?.RemoveItem(item);
    }

    // Items-grid chance committed (popup closed / Enter): persist that one item once. Read the value straight
    // off the ChanceField — the Value↔Chance two-way binding does not write back across the popup namescope,
    // so we sync it explicitly here before committing.
    private void OnItemChanceCommitted(object sender, System.EventArgs e)
    {
        if (sender is ChanceField { DataContext: PresetItemVm item } cf)
        {
            item.Chance = cf.Value;
            item.Commit();
        }
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
