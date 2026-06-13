using System.Windows;
using System.Windows.Controls;
using Dzl.Tray;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>Dashboard page: server/client control cards, launch-command previews, active-mod
/// lists and edit shortcuts. All state lives on <see cref="MainViewModel"/> (the inherited
/// DataContext); the two shortcuts open the per-server modal editor.</summary>
public partial class DashboardView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public DashboardView() => InitializeComponent();

    /// <summary>"Edit mods" shortcut → open the active server's editor on the Mods tab.</summary>
    private void OnEditMods(object sender, RoutedEventArgs e) => OpenServerEditor(1);

    /// <summary>"Edit params" shortcut → open the active server's editor on the Params tab.</summary>
    private void OnEditParams(object sender, RoutedEventArgs e) => OpenServerEditor(2);

    /// <summary>Open the modal editor for the active server on a given tab (0=Settings,1=Mods,2=Params).</summary>
    private void OpenServerEditor(int tab)
    {
        if (Vm is null) return;
        var dlg = new ServerEditorWindow(Vm, tab) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        Vm.RefreshServers();   // name/active may have changed (rename/clone)
    }
}
