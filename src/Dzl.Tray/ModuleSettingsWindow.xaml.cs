using System.IO;
using System.Windows;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>
/// Per-module settings modal — opened from a page's ⚙ button. Shows only the config a single module needs, as
/// the same global values (saving applies immediately, like the Settings page). Covers the Mods and Workshop
/// modules; more slot in via the <c>module</c> switch.
/// </summary>
public partial class ModuleSettingsWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly string _module;

    public ModuleSettingsWindow(MainViewModel vm, string module)
    {
        _vm = vm;
        _module = module;
        InitializeComponent();
        var title = _module switch { "mods" => "Mods settings", "workshop" => "Workshop settings", _ => "Settings" };
        Title = title;
        TitleBarCtl.Title = title;
        Load();
    }

    private void Load()
    {
        var c = _vm.Cfg;
        if (_module == "mods")
        {
            ModsPanel.Visibility = Visibility.Visible;
            CfgProjectsRoot.Text = c.ProjectsRoot;
            CfgWorkDriveSource.Text = c.WorkDriveSource;
            CfgAutomountWorkDrive.IsChecked = c.AutomountWorkDrive;
            CfgScanRoots.Text = string.Join("\n", c.ScanRoots);
            CfgSigningKey.ItemsSource = _vm.ListSigningKeys().Select(k => k.Name).ToList();
            CfgSigningKey.Text = !string.IsNullOrWhiteSpace(c.SigningKey) ? c.SigningKey : _vm.CachedAuthor;
            CfgKeysDir.Text = c.KeysDir;
            SigningStatus.Text = SigningKeysUi.Status(_vm, CfgSigningKey.Text);
        }
        else if (_module == "workshop")
        {
            WorkshopPanel.Visibility = Visibility.Visible;
            CfgSteamCmdPath.Text = c.SteamCmdPath;
            CfgSteamLogin.Text = c.SteamLogin;
            CfgWorkshopDir.Text = c.WorkshopDir;
            var def = Dzl.Core.Projects.ProjectPaths.WorkshopDir(Dzl.Core.Projects.ProjectPaths.Root(c));
            WorkshopDirHint.Text = $"Where steamcmd downloads land. Blank = {def}";
            RefreshSteamStatus();
        }
    }

    private void RefreshSteamStatus() =>
        SteamStatus.Text = _vm.SteamSignedIn
            ? "✓ Signed in — Subscribe works in-app."
            : "Not signed in — Subscribe opens the Steam page instead.";

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target }) return;
        var current = target switch
        {
            "CfgProjectsRoot" => CfgProjectsRoot.Text,
            "CfgWorkDriveSource" => CfgWorkDriveSource.Text,
            "CfgWorkshopDir" => CfgWorkshopDir.Text,
            "CfgKeysDir" => CfgKeysDir.Text,
            _ => "",
        };
        var dlg = new OpenFolderDialog();
        var start = BrowseStartDir.Resolve(current, isFile: false,
            new[] { _vm.ProjectsRoot, _vm.Cfg.DayzPath }, Directory.Exists);
        if (!string.IsNullOrEmpty(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(this) != true) return;
        if (target == "CfgProjectsRoot") CfgProjectsRoot.Text = dlg.FolderName;
        else if (target == "CfgWorkDriveSource") CfgWorkDriveSource.Text = dlg.FolderName;
        else if (target == "CfgWorkshopDir") CfgWorkshopDir.Text = dlg.FolderName;
        else if (target == "CfgKeysDir") CfgKeysDir.Text = dlg.FolderName;
    }

    // Signing-key flows shared with the global Settings page (SigningKeysUi keeps the guards in sync).
    private void OnGenerateKey(object sender, RoutedEventArgs e)
    {
        var (name, status) = SigningKeysUi.GenerateInteractive(this, _vm, CfgKeysDir.Text);
        if (name is null) return;
        CfgSigningKey.ItemsSource = _vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = name;
        SigningStatus.Text = status;
    }

    private void OnImportKeys(object sender, RoutedEventArgs e)
    {
        var (name, status) = SigningKeysUi.ImportInteractive(this, _vm, CfgKeysDir.Text);
        if (name is null) { if (status.Length > 0) SigningStatus.Text = status; return; }
        CfgSigningKey.ItemsSource = _vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = name;
        SigningStatus.Text = status;
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var start = BrowseStartDir.Resolve(CfgSteamCmdPath.Text, isFile: true,
            new[] { _vm.Cfg.DayzPath }, Directory.Exists);
        var dlg = new OpenFileDialog
        {
            Filter = "steamcmd.exe|steamcmd.exe|Executables|*.exe|All files|*.*",
            InitialDirectory = start,
        };
        if (dlg.ShowDialog(this) == true) CfgSteamCmdPath.Text = dlg.FileName;
    }

    private void OnSteamSignIn(object sender, RoutedEventArgs e)
    {
        new SteamLoginWindow(_vm) { Owner = this }.ShowDialog();
        _vm.RefreshSteamAccount();
        _vm.NotifyWorkshopGate();
        RefreshSteamStatus();
    }

    private void OnSteamSignOut(object sender, RoutedEventArgs e)
    {
        _vm.SteamSignOut();
        _vm.RefreshSteamAccount();
        _vm.NotifyWorkshopGate();
        RefreshSteamStatus();
    }

    private async void OnInstallSteamCmd(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn) btn.IsEnabled = false;
        SteamCmdStatus.Text = "installing steamcmd…";
        var (ok, path, msg) = await _vm.InstallSteamCmdAsync();
        if (ok) CfgSteamCmdPath.Text = path;
        SteamCmdStatus.Text = (ok ? "✓ " : "✗ ") + msg + (ok ? "  (Save to apply)" : "");
        if (sender is Wpf.Ui.Controls.Button b) b.IsEnabled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_module == "mods")
        {
            var root = CfgProjectsRoot.Text.Trim();
            if (root.Length == 0) { StatusText.Text = "Projects root is required."; return; }
            var roots = CfgScanRoots.Text.Replace("\r\n", "\n").Split('\n')
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            _vm.ApplyConfig(_vm.Cfg with
            {
                ProjectsRoot = root,
                WorkDriveSource = CfgWorkDriveSource.Text.Trim(),
                AutomountWorkDrive = CfgAutomountWorkDrive.IsChecked == true,
                ScanRoots = roots,
                SigningKey = CfgSigningKey.Text.Trim(),
                KeysDir = CfgKeysDir.Text.Trim(),
            });
        }
        else if (_module == "workshop")
        {
            _vm.ApplyConfig(_vm.Cfg with
            {
                SteamCmdPath = CfgSteamCmdPath.Text.Trim(),
                SteamLogin = CfgSteamLogin.Text.Trim(),
                WorkshopDir = CfgWorkshopDir.Text.Trim(),
            });
        }
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
