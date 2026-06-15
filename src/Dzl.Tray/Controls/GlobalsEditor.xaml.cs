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

    // Add only a known-but-missing engine global (globals.xml is a closed vocabulary).
    private void OnAddKnownClick(object sender, RoutedEventArgs e) => Vm?.AddKnown();

    // Per-row trash — only shown for custom (non-standard) vars; standard ones aren't removable.
    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GlobalVarRowVm row) Vm?.RemoveVar(row);
    }

    // Per-row reset — shown for standard vars: revert to the engine default instead of deleting.
    private void OnResetRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GlobalVarRowVm row) Vm?.ResetToDefault(row);
    }

    // Detail-pane reset: revert the currently selected standard var to its engine default.
    private void OnResetClick(object sender, RoutedEventArgs e) => Vm?.ResetToDefault(null);

    // Inline rename (detail pane): Enter or the Rename button commits RenameText.
    private void OnRenameClick(object sender, RoutedEventArgs e) => Vm?.CommitRename();

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.CommitRename(); e.Handled = true; }
    }

    // Detail Type combo / Value box commit → persist onto the selected var.
    private void OnDetailTypeChanged(object sender, SelectionChangedEventArgs e) => Vm?.SaveDetail();

    private void OnDetailValueLostFocus(object sender, RoutedEventArgs e) => Vm?.SaveDetail();

    private void OnDetailValueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.SaveDetail(); e.Handled = true; }
    }
}
