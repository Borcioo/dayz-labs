using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The "Event Spawns" editor (cfgeventspawns.xml). Presentational only — forwards to the bound
/// <see cref="EventSpawnsVm"/>. DataContext = an <see cref="EventSpawnsVm"/>.</summary>
public partial class EventSpawnsEditor : UserControl
{
    public EventSpawnsEditor()
    {
        InitializeComponent();
    }

    private EventSpawnsVm? Vm => DataContext as EventSpawnsVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddEventClick(object sender, RoutedEventArgs e) => Vm?.AddEvent();

    private void OnAddEventKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddEvent(); e.Handled = true; }
    }

    private void OnRemoveEventClick(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: EventSpawnRowVm ev })
        {
            vm.SelectedEvent = ev;
            vm.RemoveSelectedEvent();
        }
    }

    private void OnPosCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not EventPosVm pos) return;
        Dispatcher.BeginInvoke(new System.Action(pos.Commit), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnRemovePosClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EventPosVm pos) Vm?.RemovePos(pos);
    }

    private void OnAddPosClick(object sender, RoutedEventArgs e)
    {
        Vm?.AddPos(Num(NewPosX), Num(NewPosZ), Num(NewPosA));
        NewPosX.Value = null;
        NewPosZ.Value = null;
        NewPosA.Value = null;
    }

    private static string Num(Wpf.Ui.Controls.NumberBox box) =>
        box.Value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "";
}
