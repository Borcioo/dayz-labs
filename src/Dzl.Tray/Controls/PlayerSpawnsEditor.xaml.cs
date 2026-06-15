using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The CE Player Spawns editor (Economy "Player Spawns" tab). Presentational only — clicks and
/// inline-edit commits forward to the bound <see cref="PlayerSpawnsVm"/>, which calls
/// <see cref="Dzl.Core.App.PlayerSpawnsService"/> and snapshots/writes each edit. DataContext = a
/// <see cref="PlayerSpawnsVm"/>.</summary>
public partial class PlayerSpawnsEditor : UserControl
{
    public PlayerSpawnsEditor()
    {
        InitializeComponent();
    }

    private PlayerSpawnsVm? Vm => DataContext as PlayerSpawnsVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    // ── groups ───────────────────────────────────────────────────────────
    private void OnAddGroupClick(object sender, RoutedEventArgs e) => Vm?.AddGroup();

    private void OnAddGroupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddGroup(); e.Handled = true; }
    }

    // Per-row remove: select that group, then reuse the confirm+remove flow.
    private void OnRowRemoveGroupClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: SpawnGroupVm grp })
        {
            vm.SelectedGroup = grp;
            vm.RemoveSelectedGroup();
        }
    }

    private void OnRenameGroupClick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.SelectedGroup is not { } grp)
        {
            if (Vm is { } v) v.Status = "✗ select a group to rename";
            return;
        }
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var next = PromptDialog.Show(owner, "Rename spawn group", $"Rename \"{grp.Name}\" to:", grp.Name);
        if (string.IsNullOrWhiteSpace(next)) return;
        vm.RenameSelectedGroup(next.Trim());
    }

    // ── positions ────────────────────────────────────────────────────────
    private void OnPosCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not SpawnPosVm pos) return;
        // Binding commits on LostFocus; defer so the edited value is written back first.
        Dispatcher.BeginInvoke(new System.Action(pos.Commit),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnRemovePosClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SpawnPosVm pos) Vm?.RemovePos(pos);
    }

    private void OnAddPosClick(object sender, RoutedEventArgs e)
    {
        Vm?.AddPos(Num(NewPosX), Num(NewPosZ));
        NewPosX.Value = null;
        NewPosZ.Value = null;
    }

    private static string Num(Wpf.Ui.Controls.NumberBox box) =>
        box.Value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "";

    // ── "Other" param rows (non-canonical keys) ──────────────────────────
    private void OnParamCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not SpawnParamVm param) return;
        Dispatcher.BeginInvoke(new System.Action(param.Commit),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
