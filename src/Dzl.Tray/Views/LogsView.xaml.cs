using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray.Views;

/// <summary>Logs page: four log panes shown in grid / tabs / focus modes; every pane auto-scrolls
/// to the tail. State lives on <see cref="MainViewModel"/> (the inherited DataContext).</summary>
public partial class LogsView : UserControl
{
    public LogsView() => InitializeComponent();

    /// <summary>Auto-scroll a log pane to the end whenever new lines arrive — but only when the pane's
    /// Auto-scroll toggle is on, so a user reading older lines isn't yanked to the tail. Wired from the
    /// log TextBox inside <c>LogPaneTemplate</c>, so it works in every view mode.</summary>
    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: LogPaneVm { AutoScroll: true } } tb) tb.ScrollToEnd();
    }
}
