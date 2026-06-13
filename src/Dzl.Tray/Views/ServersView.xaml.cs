using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Servers;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>Servers page (instances): create a server, activate one, and open its modal editor
/// (Settings / Mods / Params tabs). All state lives on <see cref="MainViewModel"/> (the inherited
/// DataContext); destructive actions confirm first.</summary>
public partial class ServersView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public ServersView() => InitializeComponent();

    // Re-entrancy guard for the create-server flow (button disabled while it runs, but a fast
    // double-tap before the first frame renders could still re-enter).
    private bool _creatingServer;

    private async void OnCreateServer(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _creatingServer) return;
        var name = NewServerNameBox.Text.Trim();
        if (name.Length == 0) { NewServerStatus.Text = "Enter an instance name."; return; }
        var map = (NewServerMapBox.SelectedItem as string) ?? "chernarus";
        int? port = int.TryParse(NewServerPortBox.Text.Trim(), out var p) ? p : null;
        var baseSel = NewServerBaseBox.SelectedItem as string;
        var baseName = (string.IsNullOrEmpty(baseSel) || baseSel == MainViewModel.VanillaChoice) ? null : baseSel;
        _creatingServer = true;
        NewServerButton.IsEnabled = false;
        NewServerStatus.Text = "creating… (copying mission template — this can take a moment)";
        try { NewServerStatus.Text = await Vm.CreateServerAsync(name, map, port, baseName); }
        catch (Exception ex) { NewServerStatus.Text = "✗ " + ex.Message; }
        finally { NewServerButton.IsEnabled = true; _creatingServer = false; }
        if (NewServerStatus.Text.StartsWith('✓')) { NewServerNameBox.Text = ""; NewServerPortBox.Text = ""; }
    }

    private void OnUseServer(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement { Tag: string name })
            NewServerStatus.Text = Vm.UseServer(name);
    }

    // A base fixes its own map (baked into its serverDZ.cfg + mpmission). When one is
    // selected, lock the map dropdown and reflect the base's map; only vanilla is free to pick.
    private void OnNewServerBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || NewServerMapBox is null) return;   // fires once during InitializeComponent before peers exist
        var sel = NewServerBaseBox.SelectedItem as string;
        var vanilla = string.IsNullOrEmpty(sel) || sel == MainViewModel.VanillaChoice;
        NewServerMapBox.IsEnabled = vanilla;
        if (!vanilla)
        {
            var b = Vm.Bases.FirstOrDefault(x => x.Name == sel);
            if (b is not null) NewServerMapBox.SelectedItem = MapAliases.MapName(b.Mission);
        }
    }

    // --- per-server modal editor -----------------------------------------

    /// <summary>Open the modal editor for the active server on a given tab (0=Settings,1=Mods,2=Params).</summary>
    private void OpenServerEditor(int tab)
    {
        if (Vm is null) return;
        var dlg = new ServerEditorWindow(Vm, tab) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        Vm.RefreshServers();   // name/active may have changed (rename/clone)
    }

    /// <summary>Servers row "Settings"/"Mods": activate the clicked server, then open its modal editor.</summary>
    private void OpenServerForRow(object sender, int tab)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string name }) return;
        Vm.UseServer(name);
        OpenServerEditor(tab);
    }

    private void OnOpenServerSettings(object sender, RoutedEventArgs e) => OpenServerForRow(sender, 0);
    private void OnOpenServerMods(object sender, RoutedEventArgs e) => OpenServerForRow(sender, 1);

    /// <summary>Open a server instance's folder in Explorer (Tag = the instance dir).</summary>
    private void OnOpenServerFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}", "Open server folder",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string folder }) return;
        var msg = Vm.OpenInEditor(folder);
        if (msg.StartsWith('✗'))
            System.Windows.MessageBox.Show(msg.TrimStart('✗', ' '), "Open in editor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnWipeServerPersistence(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        var ok = System.Windows.MessageBox.Show(
            $"Wipe persistence for this server?\n\n{dir}\n\nThe world / loot / player state resets; DayZ " +
            "regenerates fresh Central Economy storage on the next start. The mission files are kept.",
            "Wipe persistence", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        NewServerStatus.Text = Vm.WipePersistenceDir(dir);
    }

    private void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string name }) return;
        var r = System.Windows.MessageBox.Show(
            $"Delete server \"{name}\"?\n\n" +
            "YES — delete the server AND all its files (serverDZ.cfg, mpmissions, profiles / logs). Cannot be undone.\n\n" +
            "NO — remove it from dzl only; keep the folder + files on disk.\n\n" +
            "CANCEL — don't delete.",
            "Delete server", System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Cancel) return;
        NewServerStatus.Text = Vm.DeleteServer(name, removeFiles: r == System.Windows.MessageBoxResult.Yes);
    }
}
