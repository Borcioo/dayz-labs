using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dzl.Tray.Controls;

/// <summary>The CE Spawnable Types editor (Economy "Spawnable Types" tab). Presentational only — clicks and
/// inline-edit commits forward to the bound <see cref="SpawnableTypesVm"/>, which calls
/// <see cref="Dzl.Core.App.SpawnableTypesService"/> and snapshots/writes each edit. DataContext = a
/// <see cref="SpawnableTypesVm"/>.</summary>
public partial class SpawnableTypesEditor : UserControl
{
    public SpawnableTypesEditor()
    {
        InitializeComponent();
    }

    private SpawnableTypesVm? Vm => DataContext as SpawnableTypesVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddTypeClick(object sender, RoutedEventArgs e) => Vm?.AddType();

    // New-type classname AutoSuggestBox raised Submitted (Enter) — add the type.
    private void OnAddTypeSubmitted(object? sender, System.EventArgs e) => Vm?.AddType();

    private void OnRemoveTypeClick(object sender, RoutedEventArgs e) => Vm?.RemoveSelectedType();

    // Per-row quick action: select that row then remove it (rename is inline in the detail header).
    private void OnRowRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: SpawnTypeRowVm row })
        {
            vm.SelectedType = row;
            vm.RemoveSelectedType();
        }
    }

    // Inline rename (detail-header AutoSuggestBox): commit on the Rename button or Enter (Submitted).
    private void OnRenameClick(object sender, RoutedEventArgs e) => Vm?.CommitRename();

    private void OnRenameSubmitted(object? sender, System.EventArgs e) => Vm?.CommitRename();

    private void OnHoarderClick(object sender, RoutedEventArgs e) => Vm?.SaveHoarder();

    private void OnDamageLostFocus(object sender, RoutedEventArgs e) => Vm?.SaveDamage();

    private void OnDamageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.SaveDamage(); e.Handled = true; }
    }

    private void OnAddCargoChanceClick(object sender, RoutedEventArgs e) => Vm?.AddChanceBlock(isAttachments: false);
    private void OnAddAttChanceClick(object sender, RoutedEventArgs e) => Vm?.AddChanceBlock(isAttachments: true);

    private void OnAddCargoPresetClick(object sender, RoutedEventArgs e) =>
        Vm?.AddPresetBlock(isAttachments: false, ComboText(CargoPresetCombo));

    private void OnAddAttPresetClick(object sender, RoutedEventArgs e) =>
        Vm?.AddPresetBlock(isAttachments: true, ComboText(AttPresetCombo));

    private static string ComboText(ComboBox cb) => (cb.Text ?? cb.SelectedItem as string ?? "").Trim();

    private void OnRemoveBlockClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SpawnBlockVm block) Vm?.RemoveBlock(block);
    }

    private void OnBlockPresetLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SpawnBlockVm block) block.CommitPreset();
    }

    private void OnBlockPresetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as FrameworkElement)?.Tag is SpawnBlockVm block)
        {
            block.CommitPreset();
            e.Handled = true;
        }
    }

    // Block chance committed via its ChanceField (popup closed / Enter). Read the value straight off the
    // control — the two-way binding does not write back across the popup namescope — then commit.
    private void OnBlockChanceFieldCommitted(object sender, System.EventArgs e)
    {
        if (sender is ChanceField { DataContext: SpawnBlockVm block } cf)
        {
            block.Chance = cf.Value;
            block.CommitChance();
        }
    }

    // Per-item chance committed via its ChanceField (read the control's value, the binding doesn't write back).
    private void OnSpawnItemChanceCommitted(object sender, System.EventArgs e)
    {
        if (sender is ChanceField { DataContext: SpawnItemVm item } cf)
        {
            item.Chance = cf.Value;
            item.Commit();
        }
    }

    private void OnItemCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not SpawnItemVm item) return;
        // The binding commits on LostFocus; defer so the edited value is written back first.
        Dispatcher.BeginInvoke(new System.Action(item.Commit),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SpawnItemVm item) return;
        var block = BlockForElement(sender as DependencyObject);
        Vm?.RemoveItem(block, item);
    }

    private void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SpawnBlockVm block) AddItem(block);
    }

    // The reusable AutoSuggestBox raised Submitted (Enter) — add the block's item.
    private void OnItemSubmitted(object? sender, System.EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpawnBlockVm block }) AddItem(block);
    }

    /// <summary>Add the block's typed classname (NewItemName, bound to the autocomplete box) at NewItemChance,
    /// then clear the box. The block carries both, so no visual-tree lookup is needed.</summary>
    private void AddItem(SpawnBlockVm block)
    {
        Vm?.AddItem(block, block.NewItemName, block.NewItemChance.ToString(System.Globalization.CultureInfo.InvariantCulture));
        block.NewItemName = "";
    }

    /// <summary>Walk up the visual tree to find the SpawnBlockVm-tagged DataGrid that owns an item button.</summary>
    private static SpawnBlockVm? BlockForElement(DependencyObject? start)
    {
        var grid = FindAncestor<DataGrid>(start);
        return grid?.Tag as SpawnBlockVm;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var cur = start;
        while (cur is not null)
        {
            if (cur is T t) return t;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return null;
    }
}
