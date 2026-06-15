using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The World "Environment" editor (cfgenvironment.xml). Presentational only — item edits forward to
/// the bound <see cref="EnvironmentVm"/>; territory files open externally via <see cref="ShellOpen"/>.
/// DataContext = an <see cref="EnvironmentVm"/>.</summary>
public partial class EnvironmentEditor : UserControl
{
    public EnvironmentEditor()
    {
        InitializeComponent();
    }

    private EnvironmentVm? Vm => DataContext as EnvironmentVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnItemLostFocus(object sender, RoutedEventArgs e) => Commit(sender);

    private void OnItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(sender); e.Handled = true; }
    }

    private static void Commit(object sender)
    {
        if (sender is FrameworkElement { DataContext: EnvItemVm i }) i.Commit();
    }

    private void OnEditFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvFileVm f) ShellOpen.Editor(f.Path);
    }

    private void OnRevealFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvFileVm f) ShellOpen.Reveal(f.Path);
    }
}
