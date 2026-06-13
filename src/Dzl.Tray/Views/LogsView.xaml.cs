using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray.Views;

/// <summary>Logs page: four log panes shown in grid / list / tabs / focus modes. The list mode
/// shares the page height between expanded panes (recomputed in code-behind); every pane
/// auto-scrolls to the tail. State lives on <see cref="MainViewModel"/> (the inherited
/// DataContext).</summary>
public partial class LogsView : UserControl
{
    public LogsView() => InitializeComponent();

    /// <summary>Auto-scroll a log pane to the end whenever new lines arrive. Wired from the
    /// log TextBox inside <c>LogPaneTemplate</c>, so it works in every view mode.</summary>
    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    /// <summary>
    /// List view (fit-to-window): recompute the four row heights so expanded panes share the
    /// available page height equally (Star) and collapsed panes shrink to their header (Auto).
    /// Wired to each CardExpander's Expanded/Collapsed events and called once after load / when
    /// switching to list view so the initial layout is correct. No outer scroll — the grid
    /// exactly fills row 1; each pane's TextBox scrolls internally.
    /// </summary>
    private void OnLogExpanderToggled(object sender, RoutedEventArgs e) => UpdateLogListRowHeights();

    /// <summary>Recompute the list-view row heights from the panes' IsExpanded state. Public so
    /// the host window can seed it on load and when the Logs page is shown.</summary>
    public void UpdateLogListRowHeights()
    {
        if (LogsListGrid is null) return; // not yet templated

        var star = new GridLength(1, GridUnitType.Star);
        LogListRow0.Height = LogExp0.IsExpanded ? star : GridLength.Auto;
        LogListRow1.Height = LogExp1.IsExpanded ? star : GridLength.Auto;
        LogListRow2.Height = LogExp2.IsExpanded ? star : GridLength.Auto;
        LogListRow3.Height = LogExp3.IsExpanded ? star : GridLength.Auto;
    }
}
