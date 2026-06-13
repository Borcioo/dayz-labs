using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>Mods library page: the Steam Workshop launcher and the discovered-mods list.
/// All state lives on <see cref="MainViewModel"/> (the inherited DataContext); enable/order is
/// per-server (Servers page), so this page is read-only apart from removing stale entries.</summary>
public partial class ModsView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public ModsView() => InitializeComponent();

    // Open the per-module settings modal (⚙). Shared with My Mods; the host keeps the global
    // Settings page in sync afterwards in case the module edits config it mirrors.
    private void OnModuleSettings(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string module }) return;
        var owner = Window.GetWindow(this);
        new ModuleSettingsWindow(Vm, module) { Owner = owner }.ShowDialog();
        (owner as MainWindow)?.SyncSettingsPage();
    }

    private void OnOpenWorkshop(object sender, RoutedEventArgs e)
        => new WorkshopWindow(Vm!).Show();   // no Owner — an owned FluentWindow (Mica) can hide its owner
}
