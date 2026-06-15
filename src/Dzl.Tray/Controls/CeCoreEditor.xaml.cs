using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>The CE "CE Config" editor (cfgeconomycore.xml). Presentational only — clicks / commits forward to
/// the bound <see cref="CeCoreVm"/>, which calls <see cref="Dzl.Core.App.CeCoreService"/> and snapshots/writes
/// each edit. DataContext = a <see cref="CeCoreVm"/>.</summary>
public partial class CeCoreEditor : UserControl
{
    public CeCoreEditor()
    {
        InitializeComponent();
    }

    private CeCoreVm? Vm => DataContext as CeCoreVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddFileClick(object sender, RoutedEventArgs e) => Vm?.AddFile();

    private void OnRemoveFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CeRoutedFile f) Vm?.RemoveFile(f);
    }

    private void OnAddDefaultClick(object sender, RoutedEventArgs e) => Vm?.AddMissingDefault();

    // Numeric default commits on focus loss / Enter (the bound Text is already updated).
    private void OnDefaultValueLostFocus(object sender, RoutedEventArgs e) => CommitDefault(sender);

    private void OnDefaultValueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitDefault(sender); e.Handled = true; }
    }

    private static void CommitDefault(object sender)
    {
        if (sender is FrameworkElement { DataContext: CeDefaultVm d }) d.Commit();
    }
}
