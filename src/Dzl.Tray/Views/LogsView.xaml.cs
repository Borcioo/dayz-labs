using System.Windows.Controls;

namespace Dzl.Tray.Views;

/// <summary>Logs page: five log panes (script/RPT/ADM/client/console) shown in grid / tabs / focus modes.
/// Each pane body is a <see cref="Controls.LogPaneControl"/> (AvalonEdit) that handles colouring, search
/// highlight, auto-scroll and clickable file refs. State lives on <see cref="ViewModels.MainViewModel"/>
/// (the inherited DataContext).</summary>
public partial class LogsView : UserControl
{
    public LogsView() => InitializeComponent();
}
