using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    private void OnParamCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not SpawnParamVm param) return;
        // Binding commits on LostFocus; defer so the edited value is written back first.
        Dispatcher.BeginInvoke(new System.Action(param.Commit),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnAddParamClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string section)
            AddParamFromRow(sender as DependencyObject, section);
    }

    private void OnAddParamKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((sender as FrameworkElement)?.Tag is string section)
        {
            AddParamFromRow(sender as DependencyObject, section);
            e.Handled = true;
        }
    }

    /// <summary>Read the name/value TextBoxes sharing the clicked control's parent Grid and add the param.</summary>
    private void AddParamFromRow(DependencyObject? near, string section)
    {
        var grid = FindAncestor<Grid>(near);
        if (grid is null) return;
        var boxes = grid.Children.OfType<Wpf.Ui.Controls.TextBox>().ToList();
        var name = boxes.ElementAtOrDefault(0)?.Text ?? "";
        var value = boxes.ElementAtOrDefault(1)?.Text ?? "";
        Vm?.AddParam(section, name, value);
        if (boxes.Count > 0) boxes[0].Text = "";
        if (boxes.Count > 1) boxes[1].Text = "";
    }

    private void OnAddGroupClick(object sender, RoutedEventArgs e) => Vm?.AddGroup();

    private void OnAddGroupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddGroup(); e.Handled = true; }
    }

    private void OnRemoveGroupClick(object sender, RoutedEventArgs e) => Vm?.RemoveSelectedGroup();

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

    private void OnPosCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not SpawnPosVm pos) return;
        Dispatcher.BeginInvoke(new System.Action(pos.Commit),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnRemovePosClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SpawnPosVm pos) Vm?.RemovePos(pos);
    }

    private void OnAddPosClick(object sender, RoutedEventArgs e) => AddPos();

    private void OnAddPosKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddPos(); e.Handled = true; }
    }

    private void AddPos()
    {
        Vm?.AddPos(NewPosX.Text ?? "", NewPosZ.Text ?? "");
        NewPosX.Text = "";
        NewPosZ.Text = "";
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
