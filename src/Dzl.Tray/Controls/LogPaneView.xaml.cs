using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Controls;

/// <summary>The reusable content of one log pane (header, filter/search toolbar, AvalonEdit body, footer).
/// Hosted both in the Logs page (grid/tabs/focus) and standalone in a detached <see cref="LogWindow"/>.</summary>
public partial class LogPaneView : UserControl
{
    public LogPaneView() => InitializeComponent();

    /// <summary>Pop this pane out into its own window (or focus it if already open).</summary>
    private void OnDetach(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogPaneVm pane && Window.GetWindow(this)?.DataContext is MainViewModel host)
            LogWindow.Detach(pane, host);
    }
}
