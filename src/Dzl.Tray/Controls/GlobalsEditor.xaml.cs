using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The CE Globals editor (Economy "Globals" tab). Presentational only — clicks and
/// inline-edit commits forward to the bound <see cref="GlobalsVm"/>, which calls
/// <see cref="Dzl.Core.App.GlobalsService"/> and snapshots/writes each edit. DataContext = a
/// <see cref="GlobalsVm"/>.</summary>
public partial class GlobalsEditor : UserControl
{
    public GlobalsEditor()
    {
        InitializeComponent();
    }

    private GlobalsVm? Vm => DataContext as GlobalsVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddVarClick(object sender, RoutedEventArgs e) => Vm?.AddVar();

    private void OnAddVarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddVar(); e.Handled = true; }
    }

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GlobalVarRowVm row && Vm is { } vm)
        {
            vm.SelectedRow = row;
            vm.RemoveSelectedVar();
        }
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not GlobalVarRowVm row) return;
        // Defer so the binding writes back the edited value before we persist.
        Dispatcher.BeginInvoke(new System.Action(() => Vm?.CommitRowEdit(row)),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
