using System.Windows.Controls;

namespace Dzl.Tray.Views;

/// <summary>MCP integration guide page. Fully VM-bound (register command, tool list, copy
/// command) — no code-behind logic; DataContext is the inherited MainViewModel.</summary>
public partial class McpView : UserControl
{
    public McpView() => InitializeComponent();
}
