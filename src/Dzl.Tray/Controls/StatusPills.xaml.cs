using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>One row of live status pills (SERVER / CLIENT / MCP / P: / GitHub / Steam), bound to
/// the inherited <see cref="ViewModels.MainViewModel"/>. Used in the main window's top bar and
/// status footer so both show the same state.</summary>
public partial class StatusPills : UserControl
{
    public StatusPills()
    {
        InitializeComponent();
    }
}
