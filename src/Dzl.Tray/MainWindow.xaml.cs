using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Config;
using Dzl.Core.Tools;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray;

/// <summary>
/// The launcher main window: top menu bar, mod checklist, server/client controls,
/// profile switcher and live log panes. Construction resolves the config path the same
/// way the tray does (<see cref="App.ConfigPath"/>) and wires a fresh
/// <see cref="MainViewModel"/> as the DataContext. Menu handlers open the Params and
/// Config dialogs and route their results back through the VM.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.ConfigPath());
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }

    // --- Config / Params dialogs ------------------------------------------

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

    // --- Profiles menu (built dynamically on open) ------------------------

    private void OnProfilesMenuOpened(object sender, RoutedEventArgs e)
    {
        ProfilesMenu.Items.Clear();
        foreach (var name in _vm.Presets)
        {
            var item = new MenuItem
            {
                Header = name,
                IsCheckable = true,
                IsChecked = name == _vm.ActivePreset,
            };
            var captured = name;
            item.Click += (_, _) => _vm.SwitchToPreset(captured);
            ProfilesMenu.Items.Add(item);
        }
        ProfilesMenu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save as current… (use the Save button)" };
        save.Click += (_, _) =>
        {
            // Reuse the existing preset box + Save command on the main window.
            if (_vm.SavePresetCommand.CanExecute(null))
                _vm.SavePresetCommand.Execute(null);
        };
        ProfilesMenu.Items.Add(save);
    }

    // --- Tools menu (built dynamically from the DayZ Tools catalog) -------

    private void OnToolsMenuOpened(object sender, RoutedEventArgs e)
    {
        ToolsMenu.Items.Clear();
        var toolsPath = _vm.Cfg.DayzToolsPath;
        var present = string.IsNullOrWhiteSpace(toolsPath)
            ? new System.Collections.Generic.List<ToolEntry>()
            : ToolCatalog.Discover(toolsPath)
                .FindAll(t => t.Exists && t.Kind == ToolKind.LaunchOnly);

        if (present.Count == 0)
        {
            ToolsMenu.Items.Add(new MenuItem { Header = "(no tools found)", IsEnabled = false });
            return;
        }
        foreach (var tool in present)
        {
            var item = new MenuItem { Header = tool.DisplayName };
            var captured = tool;
            item.Click += (_, _) => Task.Run(() => ToolLauncher.Launch(captured));
            ToolsMenu.Items.Add(item);
        }
    }

    // --- Open folders ------------------------------------------------------

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

    private void OnToggleMode(object sender, RoutedEventArgs e)
    {
        if (_vm.ToggleModeCommand.CanExecute(null))
            _vm.ToggleModeCommand.Execute(null);
    }
}
