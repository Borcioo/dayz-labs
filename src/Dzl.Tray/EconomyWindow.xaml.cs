using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Modeless host for the Central Economy editor (<see cref="Controls.EconomyPanel"/>).
/// Deliberately has NO Owner: an owned Mica FluentWindow hides its owner when closed, and the
/// editor must not block the main window (multi-window workflow). All editor state lives on the
/// shared <see cref="MainViewModel"/>, so closing this window loses nothing.</summary>
public partial class EconomyWindow : FluentWindow
{
    public EconomyWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
