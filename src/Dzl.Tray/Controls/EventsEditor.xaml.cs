using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The CE Events editor (Economy "Events" tab). Presentational only — clicks and inline-edit
/// commits forward to the bound <see cref="EventsVm"/>, which calls <see cref="Dzl.Core.App.EventsService"/>
/// and snapshots/writes each edit. DataContext = an <see cref="EventsVm"/>.</summary>
public partial class EventsEditor : UserControl
{
    public EventsEditor()
    {
        InitializeComponent();
    }

    private EventsVm? Vm => DataContext as EventsVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddEventClick(object sender, RoutedEventArgs e) => Vm?.AddEvent();

    private void OnAddEventKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddEvent(); e.Handled = true; }
    }

    // Per-row remove: select that row, then reuse the confirm+remove flow.
    private void OnRowRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: EventRowVm row })
        {
            vm.SelectedEvent = row;
            vm.RemoveSelectedEvent();
        }
    }

    // Inline rename (detail pane): Enter or the Rename button commits RenameText.
    private void OnRenameClick(object sender, RoutedEventArgs e) => Vm?.CommitRename();

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.CommitRename(); e.Handled = true; }
    }

    // Scalar fields persist on LostFocus, keyed by the field name stored in each control's Tag.
    private void OnScalarLostFocus(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (sender is not FrameworkElement fe) return;
        var field = fe.Tag as string ?? "";
        if (string.IsNullOrEmpty(field)) return;

        var rawValue = field switch
        {
            "nominal"        => vm.DetailNominal,
            "min"            => vm.DetailMin,
            "max"            => vm.DetailMax,
            "lifetime"       => vm.DetailLifetime,
            "restock"        => vm.DetailRestock,
            "saferadius"     => vm.DetailSafeRadius,
            "distanceradius" => vm.DetailDistanceRadius,
            "cleanupradius"  => vm.DetailCleanupRadius,
            _                => null,
        };
        if (rawValue is null) return;
        vm.SaveScalar(field, rawValue);
    }

    private void OnPositionLostFocus(object sender, RoutedEventArgs e) => Vm?.SavePosition(Vm.DetailPosition);
    private void OnLimitLostFocus(object sender, RoutedEventArgs e)    => Vm?.SaveLimit(Vm.DetailLimit);

    private void OnActiveChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is CheckBox cb && cb.IsChecked.HasValue)
            vm.SaveActive(cb.IsChecked.Value);
    }

    private void OnFlagChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (sender is not CheckBox cb) return;
        var flag = cb.Tag as string ?? "";
        if (string.IsNullOrEmpty(flag)) return;
        var value = cb.IsChecked ?? false;
        vm.SaveFlag(flag, value);
    }

    private void OnChildCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not EventChildRowVm child) return;
        // Defer so the binding writes back the edited value first.
        Dispatcher.BeginInvoke(new System.Action(() => Vm?.CommitChildEdit(child)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnRemoveChildClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EventChildRowVm child) Vm?.RemoveChild(child);
    }

    private void OnAddChildClick(object sender, RoutedEventArgs e) => Vm?.AddChild();

    private void OnAddChildKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddChild(); e.Handled = true; }
    }
}
