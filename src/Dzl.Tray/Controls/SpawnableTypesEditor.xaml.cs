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

    private void OnAddTypeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddType(); e.Handled = true; }
    }

    private void OnRemoveTypeClick(object sender, RoutedEventArgs e) => Vm?.RemoveSelectedType();

    // Per-row quick actions: select that row, then reuse the rename/remove flow.
    private void OnRowRenameClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: SpawnTypeRowVm row })
        {
            vm.SelectedType = row;
            OnRenameTypeClick(sender, e);
        }
    }

    private void OnRowRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: SpawnTypeRowVm row })
        {
            vm.SelectedType = row;
            vm.RemoveSelectedType();
        }
    }

    private void OnRenameTypeClick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.SelectedType is not { } row)
        {
            if (Vm is { } v) v.Status = "✗ select a type to rename";
            return;
        }
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var next = PromptDialog.Show(owner, "Rename spawnable type", $"Rename \"{row.Name}\" to:", row.Name);
        if (string.IsNullOrWhiteSpace(next)) return;
        vm.RenameSelectedType(next.Trim());
    }

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

    private void OnBlockModePresetChecked(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if ((sender as FrameworkElement)?.Tag is not SpawnBlockVm block) return;
        if (block.IsPreset) return; // already preset (initial bind / no real change)
        // Default to the first matching preset name when switching to preset mode.
        var names = block.IsAttachments ? vm.AttachmentsPresetNames : vm.CargoPresetNames;
        var preset = names.FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(preset)) { vm.Status = "✗ no presets available — add one on the Random Presets tab"; return; }
        vm.SetBlockPreset(block, preset);
    }

    private void OnBlockModeChanceChecked(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if ((sender as FrameworkElement)?.Tag is not SpawnBlockVm block) return;
        if (block.IsChance) return; // already chance
        vm.SetBlockChance(block, "1.0");
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

    private void OnBlockChanceLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SpawnBlockVm block) block.CommitChance();
    }

    private void OnBlockChanceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as FrameworkElement)?.Tag is SpawnBlockVm block)
        {
            block.CommitChance();
            e.Handled = true;
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
        if ((sender as FrameworkElement)?.Tag is not SpawnBlockVm block) return;
        AddItemFromRow(sender as DependencyObject, block);
    }

    private void OnAddItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((sender as FrameworkElement)?.Tag is not SpawnBlockVm block) return;
        AddItemFromRow(sender as DependencyObject, block);
        e.Handled = true;
    }

    /// <summary>Find the add-item TextBoxes that share the clicked control's parent Grid, read them and add.</summary>
    private void AddItemFromRow(DependencyObject? near, SpawnBlockVm block)
    {
        var grid = FindAncestor<Grid>(near);
        if (grid is null) { Vm?.AddItem(block, "", "1.0"); return; }
        var boxes = grid.Children.OfType<Wpf.Ui.Controls.TextBox>().ToList();
        var name = boxes.ElementAtOrDefault(0)?.Text ?? "";
        var chance = boxes.ElementAtOrDefault(1)?.Text ?? "1.0";
        Vm?.AddItem(block, name, chance);
        if (boxes.Count > 0) boxes[0].Text = "";
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
