using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Config;
using Dzl.Core.Tools;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray;

/// <summary>
/// The launcher main window: a Wpf.Ui <see cref="FluentWindow"/> with a title bar, a
/// persistent top action bar (mode toggle, profile switcher, server/client status pills)
/// and a left ListBox-based nav rail that swaps between five content panels
/// (Dashboard, Mods, Logs, Tools, Settings). All five panels are fully built; the Logs,
/// Tools and Settings pages own their interaction logic here (auto-scroll, file/folder
/// pickers, background tool runs and inline config/params editing — no modal dialogs).
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

        // Select Dashboard on load so a panel is always visible; selecting the first
        // NavTop item raises OnNavChanged, which calls ShowPage("dashboard"). Also seed the
        // Logs list-view row heights from the panes' initial IsExpanded state.
        Loaded += (_, _) =>
        {
            NavTop.SelectedIndex = 0;
            UpdateLogListRowHeights();
        };
    }

    // --- Navigation: swap the visible content panel based on the selected rail item ---

    /// <summary>True while we are programmatically restoring the rail selection (after the
    /// "Setup" pseudo-item opened the wizard) so the resulting SelectionChanged is ignored.</summary>
    private bool _restoringNav;

    /// <summary>Tag of the last real content page shown, so "Setup" (a button-like item) can
    /// restore the rail to it instead of becoming a sticky selection.</summary>
    private string _currentPageTag = "dashboard";

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringNav) return;
        if (sender is ListBox lb && lb.SelectedItem is ListBoxItem { Tag: string tag })
        {
            // "Setup" is not a page: open the wizard, then bounce the selection back.
            if (tag == "setup")
            {
                OpenSetupWizard();
                RestoreNavToCurrentPage();
                return;
            }

            // Clear the other rail's selection so only one item looks active.
            if (ReferenceEquals(lb, NavTop)) NavBottom.SelectedItem = null;
            else NavTop.SelectedItem = null;
            _currentPageTag = tag;
            ShowPage(tag);
        }
    }

    /// <summary>Reselect the rail item for <see cref="_currentPageTag"/> without re-running page
    /// logic (guarded so the programmatic SelectionChanged is a no-op).</summary>
    private void RestoreNavToCurrentPage()
    {
        _restoringNav = true;
        try
        {
            NavTop.SelectedItem = null;
            NavBottom.SelectedItem = null;
            if (!TrySelect(NavTop, _currentPageTag))
                TrySelect(NavBottom, _currentPageTag);
        }
        finally { _restoringNav = false; }
    }

    private static bool TrySelect(ListBox rail, string tag)
    {
        foreach (var obj in rail.Items)
            if (obj is ListBoxItem { Tag: string t } item && t == tag)
            {
                rail.SelectedItem = item;
                return true;
            }
        return false;
    }

    private void ShowPage(string tag)
    {
        if (PageDashboard is null) return; // not yet templated
        PageDashboard.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PageMods.Visibility = tag == "mods" ? Visibility.Visible : Visibility.Collapsed;
        PageMyMods.Visibility = tag == "mymods" ? Visibility.Visible : Visibility.Collapsed;
        PageServers.Visibility = tag == "servers" ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PageMcp.Visibility = tag == "mcp" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Refresh page-local state on show.
        if (tag == "logs") UpdateLogListRowHeights();
        if (tag == "tools") RefreshToolsPage();
        if (tag == "mymods") { _vm.RefreshModProjects(); if (NewModAuthorBox.Text.Length == 0) NewModAuthorBox.Text = _vm.CachedAuthor; }
        if (tag == "servers") { _vm.RefreshServers(); LoadServerEditor(); LoadParamsEditor(); }
        if (tag == "settings") LoadSettingsFields();
    }

    // --- Dashboard shortcut handlers --------------------------------------

    /// <summary>"Edit mods" shortcut → the Servers page (the active server's mod loadout lives there).</summary>
    private void OnEditMods(object sender, RoutedEventArgs e) => SelectNavTop("servers");

    /// <summary>"Edit params" shortcut → the Servers page; pre-select the params target
    /// (Server/Client) for the column whose button was clicked (Tag), then scroll to it.</summary>
    private void OnEditParams(object sender, RoutedEventArgs e)
    {
        SelectNavTop("servers");   // raises OnNavChanged → ShowPage("servers") → loads editor + params

        var target = (sender as FrameworkElement)?.Tag as string;
        if (ParamTarget is not null && target is "server" or "client")
        {
            ParamTarget.SelectedIndex = target == "client" ? 1 : 0; // 0=server, 1=client
            LoadParamsEditor();
        }

        Dispatcher.BeginInvoke(new Action(() => ParamsCard?.BringIntoView()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Select the NavTop rail item whose Tag matches (raises OnNavChanged).</summary>
    private void SelectNavTop(string tag)
    {
        foreach (var obj in NavTop.Items)
            if (obj is ListBoxItem { Tag: string t } item && t == tag)
            {
                NavTop.SelectedItem = item;
                return;
            }
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

    // === LOGS page ========================================================

    /// <summary>Auto-scroll a log pane to the end whenever new lines arrive. Wired from the
    /// log TextBox inside <c>LogPaneTemplate</c>, so it works in every view mode.</summary>
    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    /// <summary>
    /// List view (fit-to-window): recompute the four row heights so expanded panes share the
    /// available page height equally (Star) and collapsed panes shrink to their header (Auto).
    /// Wired to each CardExpander's Expanded/Collapsed events and called once after load / when
    /// switching to list view so the initial layout is correct. No outer scroll — the grid
    /// exactly fills PageLogs row 1; each pane's TextBox scrolls internally.
    /// </summary>
    private void OnLogExpanderToggled(object sender, RoutedEventArgs e) => UpdateLogListRowHeights();

    private void UpdateLogListRowHeights()
    {
        if (LogsListGrid is null) return; // not yet templated

        var star = new GridLength(1, GridUnitType.Star);
        LogListRow0.Height = LogExp0.IsExpanded ? star : GridLength.Auto;
        LogListRow1.Height = LogExp1.IsExpanded ? star : GridLength.Auto;
        LogListRow2.Height = LogExp2.IsExpanded ? star : GridLength.Auto;
        LogListRow3.Height = LogExp3.IsExpanded ? star : GridLength.Auto;
    }

    // Open-folder and clear are now VM commands (OpenLogFolderCommand/ClearLogCommand) bound
    // directly from LogPaneTemplate with the pane as CommandParameter.

    // === TOOLS page =======================================================

    private void RefreshToolsPage()
    {
        _vm.RefreshTools();
        PackToolMissing.Visibility = _vm.ToolExe("addonbuilder") is null ? Visibility.Visible : Visibility.Collapsed;
        PackButton.IsEnabled = _vm.ToolExe("addonbuilder") is not null;
        PaaToolMissing.Visibility = _vm.ToolExe("imagetopaa") is null ? Visibility.Visible : Visibility.Collapsed;
        PaaButton.IsEnabled = _vm.ToolExe("imagetopaa") is not null;
        UnbinToolMissing.Visibility = _vm.ToolExe("cfgconvert") is null ? Visibility.Visible : Visibility.Collapsed;
        UnbinButton.IsEnabled = _vm.ToolExe("cfgconvert") is not null;
    }

    private void OnRefreshTools(object sender, RoutedEventArgs e) => RefreshToolsPage();
    private void OnRefreshWorkDrive(object sender, RoutedEventArgs e) => _vm.RefreshWorkDrive();

    private void OnLaunchTool(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToolEntry t }) _vm.LaunchTool(t);
    }

    private void OnMountWorkDrive(object sender, RoutedEventArgs e) => _vm.MountWorkDrive();
    private void OnUnmountWorkDrive(object sender, RoutedEventArgs e) => _vm.UnmountWorkDrive();

    private async void OnPackPbo(object sender, RoutedEventArgs e)
    {
        var exe = _vm.ToolExe("addonbuilder");
        if (exe is null) { PackOutput.Text = "Addon Builder not found."; return; }
        var src = PackSrcBox.Text.Trim();
        var dst = PackDstBox.Text.Trim();
        if (src.Length == 0 || dst.Length == 0) { PackOutput.Text = "Pick a source and output folder."; return; }

        PackButton.IsEnabled = false;
        PackOutput.Text = "Packing…";
        try
        {
            var r = await _vm.PackAsync(exe, src, dst, PackPrefixBox.Text, PackSignBox.Text);
            PackOutput.Text = $"{(r.Ok ? "OK" : $"FAILED (exit {r.ExitCode})")}\n{r.Output}";
        }
        catch (Exception ex) { PackOutput.Text = "Error: " + ex.Message; }
        finally { PackButton.IsEnabled = true; PackOutput.ScrollToEnd(); }
    }

    private async void OnConvertPaa(object sender, RoutedEventArgs e)
    {
        var exe = _vm.ToolExe("imagetopaa");
        if (exe is null) { PaaOutput.Text = "ImageToPAA not found."; return; }
        var dir = PaaDirBox.Text.Trim();
        if (dir.Length == 0) { PaaOutput.Text = "Pick an image folder."; return; }
        var recursive = PaaRecursive.IsChecked == true;

        // First surface suffix warnings from the plan.
        var plan = _vm.PlanPaa(dir, recursive);
        if (plan.Count == 0) { PaaOutput.Text = "No .png/.tga files found."; return; }
        var warnings = plan.Where(j => !j.SuffixOk).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{plan.Count} file(s) to convert.");
        if (warnings.Count > 0)
        {
            sb.AppendLine($"⚠ {warnings.Count} file(s) lack a known texture suffix (_co/_nohq/…):");
            foreach (var w in warnings) sb.AppendLine("  " + Path.GetFileName(w.Input));
        }
        sb.AppendLine("Converting…");
        PaaOutput.Text = sb.ToString();

        PaaButton.IsEnabled = false;
        var ok = 0; var fail = 0;
        var progress = new Progress<PaaResult>(r =>
        {
            if (r.Ok) ok++; else fail++;
            PaaOutput.AppendText($"{(r.Ok ? "  ok " : "  ✗  ")}{Path.GetFileName(r.Input)} — {r.Message}\n");
            PaaOutput.ScrollToEnd();
        });
        try
        {
            await _vm.ConvertPaaAsync(exe, dir, recursive, progress);
            PaaOutput.AppendText($"Done. {ok} ok, {fail} failed.\n");
        }
        catch (Exception ex) { PaaOutput.AppendText("Error: " + ex.Message + "\n"); }
        finally { PaaButton.IsEnabled = true; PaaOutput.ScrollToEnd(); }
    }

    private async void OnUnbinarize(object sender, RoutedEventArgs e)
    {
        var exe = _vm.ToolExe("cfgconvert");
        if (exe is null) { UnbinOutput.Text = "CfgConvert not found."; return; }
        var bin = BinFileBox.Text.Trim();
        if (bin.Length == 0) { UnbinOutput.Text = "Pick a .bin file."; return; }
        var outCpp = Path.ChangeExtension(bin, ".cpp");

        UnbinButton.IsEnabled = false;
        UnbinOutput.Text = "Unbinarizing…";
        try
        {
            var (ok, output) = await _vm.UnbinarizeAsync(exe, bin, outCpp);
            UnbinOutput.Text = $"{(ok ? "OK → " + outCpp : "FAILED")}\n{output}";
        }
        catch (Exception ex) { UnbinOutput.Text = "Error: " + ex.Message; }
        finally { UnbinButton.IsEnabled = true; UnbinOutput.ScrollToEnd(); }
    }

    // Folder picker → set the target TextBox text (Tag picks which one).
    private void OnBrowseFolderInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string which }) return;
        var dir = PickFolder();
        if (dir is null) return;
        switch (which)
        {
            case "packsrc": PackSrcBox.Text = dir; break;
            case "packdst": PackDstBox.Text = dir; break;
            case "paadir": PaaDirBox.Text = dir; break;
        }
    }

    private void OnBrowseBinFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Binarized config (*.bin)|*.bin|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true) BinFileBox.Text = dlg.FileName;
    }

    // === SETTINGS page ====================================================

    // Settings = machine-global only. Per-server fields live on the Servers page (LoadServerEditor).
    private void LoadSettingsFields()
    {
        var c = _vm.Cfg;
        CfgDayzPath.Text = c.DayzPath;
        CfgDayzToolsPath.Text = c.DayzToolsPath;
        CfgProjectsRoot.Text = c.ProjectsRoot;
        CfgExeDebug.Text = c.ExeDebug;
        CfgExeNormal.Text = c.ExeNormal;
        CfgClientExeDebug.Text = c.ClientExeDebug;
        CfgClientExeNormal.Text = c.ClientExeNormal;
        CfgScanRoots.Text = string.Join("\n", c.ScanRoots);
        CfgEnableAutomationServer.IsChecked = c.EnableAutomationServer;
        ConfigError.Visibility = Visibility.Collapsed;
    }

    private void OnRevertConfig(object sender, RoutedEventArgs e) => LoadSettingsFields();

    private void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        ConfigError.Visibility = Visibility.Collapsed;
        var roots = CfgScanRoots.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        var edited = _vm.Cfg with
        {
            DayzPath = CfgDayzPath.Text.Trim(),
            DayzToolsPath = CfgDayzToolsPath.Text.Trim(),
            ProjectsRoot = CfgProjectsRoot.Text.Trim(),
            ExeDebug = CfgExeDebug.Text.Trim(),
            ExeNormal = CfgExeNormal.Text.Trim(),
            ClientExeDebug = CfgClientExeDebug.Text.Trim(),
            ClientExeNormal = CfgClientExeNormal.Text.Trim(),
            ScanRoots = roots,
            EnableAutomationServer = CfgEnableAutomationServer.IsChecked == true,
        };
        _vm.ApplyConfig(edited);
        LoadSettingsFields();
    }

    // === SERVERS page: active-instance settings editor ====================

    private void LoadServerEditor()
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

    private void OnRevertServer(object sender, RoutedEventArgs e) => LoadServerEditor();

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
        LoadServerEditor();
        LoadParamsEditor();
    }

    private void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var ok = System.Windows.MessageBox.Show(
            $"Delete server instance \"{name}\"?\n\nThis removes its config + preset (the serverDZ.cfg / mission files on disk are left in place).",
            "Delete server", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        NewServerStatus.Text = _vm.DeleteServer(name);
        LoadServerEditor();
    }

    private void OnCloneServer(object sender, RoutedEventArgs e)
    {
        var name = SrvCloneBox.Text.Trim();
        if (name.Length == 0) { SrvError.Text = "Enter a name to clone as."; SrvError.Visibility = Visibility.Visible; return; }
        NewServerStatus.Text = _vm.CloneActive(name);
        LoadServerEditor();
    }

    private void OnRenameServer(object sender, RoutedEventArgs e)
    {
        var name = SrvRenameBox.Text.Trim();
        if (name.Length == 0) { SrvError.Text = "Enter a new name."; SrvError.Visibility = Visibility.Visible; return; }
        NewServerStatus.Text = _vm.RenameActive(name);
        LoadServerEditor();
    }

    private void OnAddScanRoot(object sender, RoutedEventArgs e)
    {
        var dir = PickFolder();
        if (dir is null) return;
        var existing = CfgScanRoots.Text.TrimEnd();
        CfgScanRoots.Text = existing.Length == 0 ? dir : existing + "\n" + dir;
    }

    /// <summary>Current edited-or-saved DayZ install dir (Settings field wins over the VM config).</summary>
    private string CurrentDayzPath()
    {
        var p = CfgDayzPath?.Text?.Trim();
        return string.IsNullOrEmpty(p) ? _vm.Cfg.DayzPath : p;
    }

    // A normalized, existing directory safe to hand to a dialog's InitialDirectory
    // (forward-slash / mixed-separator paths crash OpenFolderDialog/OpenFileDialog). "" = use default.
    private static string SafeInitialDir(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            try { var full = Path.GetFullPath(c); if (Directory.Exists(full)) return full; }
            catch { /* skip bad path */ }
        }
        return "";
    }

    // Mission folder picker → write a rel-or-abs path (relative to DayzPath when under it).
    private void OnBrowseMission(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var dlg = new OpenFolderDialog
        {
            InitialDirectory = SafeInitialDir(Path.Combine(dayz, "mpmissions"), dayz),
        };
        if (dlg.ShowDialog(this) == true)
            CfgMission.Text = RelOrAbs(dlg.FolderName, dayz);
    }

    // Server config (*.cfg) file picker → write a rel-or-abs path (relative to DayzPath when under it).
    private void OnBrowseConfigName(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var dlg = new OpenFileDialog
        {
            Filter = "Server config (*.cfg)|*.cfg|All files (*.*)|*.*",
            InitialDirectory = SafeInitialDir(dayz),
        };
        if (dlg.ShowDialog(this) == true)
            CfgConfigName.Text = RelOrAbs(dlg.FileName, dayz);
    }

    /// <summary>
    /// Store <paramref name="fullPath"/> relative to <paramref name="dayzPath"/> when it lives under
    /// the DayZ install dir, else the absolute path. Mirrors how Core's ArgvBuilder resolves
    /// profiles paths so -mission=/-config= stay portable against the DayZ working dir.
    /// </summary>
    private static string RelOrAbs(string fullPath, string dayzPath)
    {
        var full = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(dayzPath);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    // --- Params editor: one editor that loads/saves the selected target+mode slot ---

    private string SelectedTarget =>
        (ParamTarget.SelectedItem as ComboBoxItem)?.Content as string ?? "server";

    private string SelectedParamMode =>
        (ParamMode.SelectedItem as ComboBoxItem)?.Content as string ?? "debug";

    private void OnParamSlotChanged(object sender, SelectionChangedEventArgs e) => LoadParamsEditor();

    private void LoadParamsEditor()
    {
        if (ParamsEditor is null) return; // not yet templated
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

    /// <summary>Re-open the environment setup wizard (Settings button entry point).</summary>
    private void OnRunSetupWizard(object sender, RoutedEventArgs e) => OpenSetupWizard();

    /// <summary>Open the Setup Wizard modally; on Finish, reload the VM so the new
    /// config/profile takes effect immediately. Shared by the Settings button and the
    /// "Setup" nav-rail item.</summary>
    private void OpenSetupWizard()
    {
        var wizard = new SetupWizardWindow(App.ConfigPath()) { Owner = this };
        if (wizard.ShowDialog() == true)
        {
            _vm.Reload();
            LoadSettingsFields();
        }
    }

    // === MY MODS page =====================================================

    private void OnRefreshMyMods(object sender, RoutedEventArgs e) => _vm.RefreshModProjects();

    private void OnCreateMod(object sender, RoutedEventArgs e)
    {
        var name = NewModNameBox.Text.Trim();
        var author = NewModAuthorBox.Text.Trim();
        if (name.Length == 0) { NewModStatus.Text = "Enter a mod name."; return; }
        NewModButton.IsEnabled = false;
        try { NewModStatus.Text = _vm.CreateModProject(name, author); }
        finally { NewModButton.IsEnabled = true; }
        if (NewModStatus.Text.StartsWith('✓')) NewModNameBox.Text = "";
    }

    private void OnImportMod(object sender, RoutedEventArgs e)
    {
        var path = ImportPathBox.Text.Trim();
        if (path.Length == 0) { ImportStatus.Text = "Pick a mod source folder."; return; }
        ImportStatus.Text = _vm.ImportModProject(path, ImportNameBox.Text);
        if (ImportStatus.Text.StartsWith('✓')) { ImportPathBox.Text = ""; ImportNameBox.Text = ""; }
    }

    private void OnQuickJunction(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            NewModStatus.Text = _vm.QuickJunction(name);
    }

    // === SERVERS page =====================================================

    private void OnRefreshServers(object sender, RoutedEventArgs e) => _vm.RefreshServers();

    private void OnCreateServer(object sender, RoutedEventArgs e)
    {
        var name = NewServerNameBox.Text.Trim();
        if (name.Length == 0) { NewServerStatus.Text = "Enter an instance name."; return; }
        var map = (NewServerMapBox.SelectedItem as string) ?? "chernarus";
        int? port = int.TryParse(NewServerPortBox.Text.Trim(), out var p) ? p : null;
        NewServerButton.IsEnabled = false;
        try { NewServerStatus.Text = _vm.CreateServer(name, map, port); }
        finally { NewServerButton.IsEnabled = true; }
        if (NewServerStatus.Text.StartsWith('✓')) { NewServerNameBox.Text = ""; NewServerPortBox.Text = ""; }
    }

    private void OnUseServer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            NewServerStatus.Text = _vm.UseServer(name);
    }

    // === Shared pickers / folder open =====================================

    /// <summary>Show a folder picker (OpenFolderDialog on .NET 8 WPF); null if cancelled.</summary>
    private string? PickFolder()
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    /// <summary>Browse into a named Settings TextBox. Tag form: "dir:&lt;FieldName&gt;".</summary>
    private void OnBrowseInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return;
        var dir = PickFolder();
        if (dir is null) return;
        if (FindName(parts[1]) is TextBox tb) tb.Text = dir;
    }
}
