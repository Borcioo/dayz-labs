using System.Windows;
using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>The CE "Economy core" editor (db/economy.xml). Presentational only — toggle/clicks forward to the
/// bound <see cref="EconomyCoreVm"/>, which calls <see cref="Dzl.Core.App.EconomyService"/> and snapshots/writes
/// each edit. DataContext = an <see cref="EconomyCoreVm"/>.</summary>
public partial class EconomyCoreEditor : UserControl
{
    public EconomyCoreEditor()
    {
        InitializeComponent();
    }

    private EconomyCoreVm? Vm => DataContext as EconomyCoreVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddKnownClick(object sender, RoutedEventArgs e) => Vm?.AddKnown();

    // Standard group: reset its flags to engine defaults (not deletable).
    private void OnResetRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EconomyGroupVm row) Vm?.ResetToDefault(row);
    }

    // Custom group only: removable.
    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EconomyGroupVm row) Vm?.RemoveGroup(row);
    }
}
