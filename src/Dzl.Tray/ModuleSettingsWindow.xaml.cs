using System.Linq;
using System.Windows;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>
/// Per-module settings modal — opened from a page's ⚙ button. Shows only the config a single module needs, as
/// the same global values (saving applies immediately, like the Settings page). Phase 1 covers the Mods module;
/// other modules slot in via the <c>module</c> switch.
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
        Title = _module == "mods" ? "Mods settings" : "Settings";
        Load();
    }

    private void Load()
    {
        // Phase 1: only the Mods module is wired; other modules will branch here.
        if (_module != "mods") return;
        var c = _vm.Cfg;
        CfgProjectsRoot.Text = c.ProjectsRoot;
        CfgWorkDriveSource.Text = c.WorkDriveSource;
        CfgAutomountWorkDrive.IsChecked = c.AutomountWorkDrive;
        CfgScanRoots.Text = string.Join("\n", c.ScanRoots);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target }) return;
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) != true) return;
        if (target == "CfgProjectsRoot") CfgProjectsRoot.Text = dlg.FolderName;
        else if (target == "CfgWorkDriveSource") CfgWorkDriveSource.Text = dlg.FolderName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var root = CfgProjectsRoot.Text.Trim();
        if (root.Length == 0)
        {
            StatusText.Text = "Projects root is required.";
            return;
        }
        var roots = CfgScanRoots.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _vm.ApplyConfig(_vm.Cfg with
        {
            ProjectsRoot = root,
            WorkDriveSource = CfgWorkDriveSource.Text.Trim(),
            AutomountWorkDrive = CfgAutomountWorkDrive.IsChecked == true,
            ScanRoots = roots,
        });
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
