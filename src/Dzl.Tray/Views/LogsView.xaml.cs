using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>Logs page: five log panes (script/RPT/ADM/client/console) shown in grid / tabs / focus modes.
/// Each pane body is a <see cref="Controls.LogPaneView"/>; a pane can be popped out into its own
/// <see cref="LogWindow"/>, in which case the page shows a "bring back" placeholder. State lives on
/// <see cref="MainViewModel"/> (the inherited DataContext).</summary>
public partial class LogsView : UserControl
{
    public LogsView() => InitializeComponent();

    /// <summary>Close a detached pane's window, re-attaching it to the page.</summary>
    private void OnReattach(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LogPaneVm pane)
            LogWindow.Reattach(pane);
    }
}
