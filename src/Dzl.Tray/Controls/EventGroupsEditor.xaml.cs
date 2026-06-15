using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The "Event Groups" editor (cfgeventgroups.xml). Presentational only — forwards to the bound
/// <see cref="EventGroupsVm"/>. DataContext = an <see cref="EventGroupsVm"/>.</summary>
public partial class EventGroupsEditor : UserControl
{
    public EventGroupsEditor()
    {
        InitializeComponent();
    }

    private EventGroupsVm? Vm => DataContext as EventGroupsVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddGroupClick(object sender, RoutedEventArgs e) => Vm?.AddGroup();

    private void OnAddGroupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddGroup(); e.Handled = true; }
    }

    private void OnRemoveGroupClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: EventGroupRowVm g })
        {
            vm.SelectedGroup = g;
            vm.RemoveSelectedGroup();
        }
    }

    private void OnAddChildClick(object sender, RoutedEventArgs e) => Vm?.AddChild();

    private void OnAddChildKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddChild(); e.Handled = true; }
    }

    private void OnRemoveChildClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EventGroupChildVm c) Vm?.RemoveChild(c);
    }

    private void OnChildCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not EventGroupChildVm child) return;
        Dispatcher.BeginInvoke(new System.Action(child.Commit), System.Windows.Threading.DispatcherPriority.Background);
    }
}
