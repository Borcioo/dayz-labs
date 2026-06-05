using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray;

/// <summary>
/// Per-server modal editor (Settings / Mods / Params tabs) for the ACTIVE server instance.
/// Opened from the Servers page; <see cref="MainViewModel"/> is shared so the loadout grid, params
/// and per-server fields all read/write the active instance. Closing returns to the (clean) Servers list.
/// </summary>
public partial class ServerEditorWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    /// <param name="tab">0 = Settings, 1 = Mods, 2 = Params.</param>
    public ServerEditorWindow(MainViewModel vm, int tab)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) =>
        {
            LoadEditor();
            LoadParamsEditor();
            Tabs.SelectedIndex = tab;
        };
    }

    private void LoadEditor()
    {
        var c = _vm.Cfg;
        CfgPort.Text = c.Port.ToString();
        CfgMission.Text = c.Mission;
        CfgConfigName.Text = c.ConfigName;
        CfgPlayerName.Text = c.PlayerName;
        CfgConnectIp.Text = c.ConnectIp;
        CfgProfilesPath.Text = c.ProfilesPath;
        CfgClientProfilesPath.Text = c.ClientProfilesPath;
        SrvMode.SelectedIndex = c.Mode == "normal" ? 1 : 0;
        SrvRenameBox.Text = "";
        SrvCloneBox.Text = "";
        SrvError.Visibility = Visibility.Collapsed;
    }

    private void OnRevertServer(object sender, RoutedEventArgs e) => LoadEditor();

    private void OnSaveServer(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CfgPort.Text.Trim(), out var port))
        {
            SrvError.Text = "Port must be an integer.";
            SrvError.Visibility = Visibility.Visible;
            return;
        }
        SrvError.Visibility = Visibility.Collapsed;
        var mode = (SrvMode.SelectedItem as ComboBoxItem)?.Content as string ?? "debug";
        var edited = _vm.Cfg with
        {
            Port = port,
            Mission = CfgMission.Text.Trim(),
            ConfigName = CfgConfigName.Text.Trim(),
            PlayerName = CfgPlayerName.Text.Trim(),
            ConnectIp = CfgConnectIp.Text.Trim(),
            ProfilesPath = CfgProfilesPath.Text.Trim(),
            ClientProfilesPath = CfgClientProfilesPath.Text.Trim(),
            Mode = mode,
        };
        _vm.SaveActiveInstance(edited);
        LoadEditor();
        LoadParamsEditor();
    }

    private void OnCloneServer(object sender, RoutedEventArgs e)
    {
        var name = SrvCloneBox.Text.Trim();
        if (name.Length == 0) { SrvError.Text = "Enter a name to clone as."; SrvError.Visibility = Visibility.Visible; return; }
        var msg = _vm.CloneActive(name);
        if (!msg.StartsWith('✓')) { SrvError.Text = msg; SrvError.Visibility = Visibility.Visible; return; }
        LoadEditor();
        LoadParamsEditor();
    }

    private void OnRenameServer(object sender, RoutedEventArgs e)
    {
        var name = SrvRenameBox.Text.Trim();
        if (name.Length == 0) { SrvError.Text = "Enter a new name."; SrvError.Visibility = Visibility.Visible; return; }
        var msg = _vm.RenameActive(name);
        if (!msg.StartsWith('✓')) { SrvError.Text = msg; SrvError.Visibility = Visibility.Visible; return; }
        LoadEditor();
        LoadParamsEditor();
    }

    // --- params editor (active instance) ---

    private string SelectedTarget => (ParamTarget.SelectedItem as ComboBoxItem)?.Content as string ?? "server";
    private string SelectedParamMode => (ParamMode.SelectedItem as ComboBoxItem)?.Content as string ?? "debug";

    private void OnParamSlotChanged(object sender, SelectionChangedEventArgs e) => LoadParamsEditor();

    private void LoadParamsEditor()
    {
        if (ParamsEditor is null) return;
        ParamsEditor.Text = string.Join("\n", _vm.CurrentParams(SelectedTarget, SelectedParamMode));
    }

    private void OnResetParams(object sender, RoutedEventArgs e) =>
        ParamsEditor.Text = string.Join("\n", MainViewModel.DefaultParams(SelectedTarget, SelectedParamMode));

    private void OnSaveParams(object sender, RoutedEventArgs e)
    {
        var lines = ParamsEditor.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _vm.ApplyParams(SelectedTarget, SelectedParamMode, lines);
    }

    // --- pickers (self-contained) ---

    private void OnBrowseInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var dir = PickFolder();
        if (dir is null) return;
        if (FindName(name) is TextBox tb) tb.Text = dir;
    }

    private void OnBrowseMission(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var dlg = new OpenFolderDialog { InitialDirectory = SafeInitialDir(Path.Combine(dayz, "mpmissions"), dayz) };
        if (dlg.ShowDialog(this) == true) CfgMission.Text = RelOrAbs(dlg.FolderName, dayz);
    }

    private void OnBrowseConfigName(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var dlg = new OpenFileDialog
        {
            Filter = "Server config (*.cfg)|*.cfg|All files (*.*)|*.*",
            InitialDirectory = SafeInitialDir(dayz),
        };
        if (dlg.ShowDialog(this) == true) CfgConfigName.Text = RelOrAbs(dlg.FileName, dayz);
    }

    private string? PickFolder()
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    private string CurrentDayzPath() => _vm.Cfg.DayzPath;

    private static string SafeInitialDir(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            try { var full = Path.GetFullPath(c); if (Directory.Exists(full)) return full; }
            catch { /* skip */ }
        }
        return "";
    }

    private static string RelOrAbs(string fullPath, string dayzPath)
    {
        var full = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(dayzPath);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    private void OnOpenInstanceFolder(object sender, RoutedEventArgs e)
    {
        var dir = _vm.ActiveServerDir;
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                System.Windows.MessageBox.Show($"Folder not found:\n{dir}", "Open server folder",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }

    private void OnWipePersistence(object sender, RoutedEventArgs e)
    {
        var ok = System.Windows.MessageBox.Show(
            "Wipe this server's persistence (Central Economy storage)?\n\nThe world / loot / player state " +
            "resets; DayZ regenerates fresh storage on the next start. The mission files are kept.",
            "Wipe persistence", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
            == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        WipeStatus.Text = _vm.WipeActivePersistence();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
