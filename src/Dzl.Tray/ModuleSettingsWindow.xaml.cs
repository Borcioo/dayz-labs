using System.Linq;
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
        }
        else if (_module == "workshop")
        {
            WorkshopPanel.Visibility = Visibility.Visible;
            CfgSteamCmdPath.Text = c.SteamCmdPath;
            CfgSteamLogin.Text = c.SteamLogin;
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
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) != true) return;
        if (target == "CfgProjectsRoot") CfgProjectsRoot.Text = dlg.FolderName;
        else if (target == "CfgWorkDriveSource") CfgWorkDriveSource.Text = dlg.FolderName;
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "steamcmd.exe|steamcmd.exe|Executables|*.exe|All files|*.*" };
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
            });
        }
        else if (_module == "workshop")
        {
            _vm.ApplyConfig(_vm.Cfg with
            {
                SteamCmdPath = CfgSteamCmdPath.Text.Trim(),
                SteamLogin = CfgSteamLogin.Text.Trim(),
            });
        }
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
