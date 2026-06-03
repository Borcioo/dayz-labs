using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>
/// The launcher main window: a Wpf.Ui <see cref="FluentWindow"/> with a title bar, a
/// persistent top action bar (mode toggle, profile switcher, server/client status pills)
/// and a left <see cref="NavigationView"/> rail that swaps between five content panels
/// (Dashboard, Mods, Logs, Tools, Settings). The Dashboard is fully built; Mods/Logs/Tools/
/// Settings are placeholders filled in later steps. Construction resolves the config path the
/// same way the tray does (<see cref="App.ConfigPath"/>) and wires a fresh
/// <see cref="MainViewModel"/> as the DataContext.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.ConfigPath());
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();

        // Show the Dashboard on load so a panel is always visible. The NavigationView
        // highlights its first item automatically once templated.
        Loaded += (_, _) => ShowPage("dashboard");
    }

    // --- Navigation: swap the visible content panel based on the selected rail item ---

    private void OnNavSelectionChanged(NavigationView sender, RoutedEventArgs args)
    {
        if (Nav.SelectedItem is NavigationViewItem { Tag: string tag })
            ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        if (PageDashboard is null) return; // not yet templated
        PageDashboard.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PageMods.Visibility = tag == "mods" ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Top action bar handlers ------------------------------------------

    private void OnModeToggleClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ToggleModeCommand.CanExecute(null))
            _vm.ToggleModeCommand.Execute(null);
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SwitchPresetCommand.CanExecute(null))
            _vm.SwitchPresetCommand.Execute(null);
    }

    // --- Config / Params dialogs (kept for the Settings page wired in a later step) ---

    private void OnConfigSettings(object sender, RoutedEventArgs e)
    {
        var dlg = new ConfigWindow(_vm.Cfg) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.ApplyConfig(dlg.Result);
    }

    private void OnServerParams(object sender, RoutedEventArgs e) => OpenParams("server");
    private void OnClientParams(object sender, RoutedEventArgs e) => OpenParams("client");

    private void OpenParams(string target)
    {
        var mode = _vm.Mode;
        var title = $"{(target == "server" ? "Server" : "Client")} params ({mode})";
        var dlg = new ParamsWindow(title, _vm.CurrentParams(target, mode),
            MainViewModel.DefaultParams(target, mode))
        { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.ApplyParams(target, mode, dlg.Result);
    }

    // --- Open folders (kept for the Settings/Tools pages wired in a later step) ---

    private void OnOpenConfigFolder(object sender, RoutedEventArgs e) =>
        OpenFolder(Path.GetDirectoryName(_vm.ConfigFilePath) ?? ".");

    private void OnOpenServerProfiles(object sender, RoutedEventArgs e) =>
        OpenFolder(_vm.Cfg.ProfilesPath);

    private void OnOpenClientProfiles(object sender, RoutedEventArgs e) =>
        OpenFolder(_vm.Cfg.ClientProfilesPath);

    private void OnOpenDayzInstall(object sender, RoutedEventArgs e) =>
        OpenFolder(_vm.Cfg.DayzPath);

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch { /* best-effort; ignore */ }
    }
}
