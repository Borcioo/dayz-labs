using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>One row of live status pills (SERVER / CLIENT / MCP / P: / GitHub / Steam), bound to
/// the inherited <see cref="ViewModels.MainViewModel"/>. Used in the main window's top bar and
/// status footer so both show the same state.</summary>
public partial class StatusPills : UserControl
{
    /// <summary>Compact rendering for the status footer: smaller dots, font and padding —
    /// the top bar uses the default (large) size.</summary>
    public static readonly System.Windows.DependencyProperty CompactProperty =
        System.Windows.DependencyProperty.Register(
            nameof(Compact), typeof(bool), typeof(StatusPills), new System.Windows.PropertyMetadata(false));

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public StatusPills()
    {
        InitializeComponent();
    }
}
