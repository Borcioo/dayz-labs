using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Config;
using Dzl.Core.Servers;
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
            NavGeneral.SelectedIndex = 0;   // Dashboard
            UpdateLogListRowHeights();
        };
    }

    /// <summary>Every nav rail (grouped sections + pinned system group). Selection is single across all of
    /// them — selecting in one clears the others.</summary>
    private ListBox[] NavRails => new[] { NavGeneral, NavServer, NavBottom };

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

            // Clear every other rail's selection so only one item looks active.
            foreach (var rail in NavRails)
                if (!ReferenceEquals(rail, lb)) rail.SelectedItem = null;
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
            foreach (var rail in NavRails) rail.SelectedItem = null;
            foreach (var rail in NavRails)
                if (TrySelect(rail, _currentPageTag)) break;
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
        PageBases.Visibility = tag == "bases" ? Visibility.Visible : Visibility.Collapsed;
        PageEconomy.Visibility = tag == "economy" ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PageMcp.Visibility = tag == "mcp" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Refresh page-local state on show.
        if (tag == "logs") UpdateLogListRowHeights();
        if (tag == "tools") RefreshToolsPage();
        if (tag == "mymods") { _vm.RefreshModProjects(); if (NewModAuthorBox.Text.Length == 0) NewModAuthorBox.Text = _vm.CachedAuthor; }
        if (tag == "servers") { _vm.RefreshServers(); _vm.RefreshBases(); }   // base dropdown needs bases
        if (tag == "bases") _vm.RefreshBases();
        if (tag == "economy") { _vm.TypesEditor.LoadTypes(); _vm.RefreshDictionaries(); RefreshTypesBackupsMenu(); }
        if (tag == "settings") { LoadSettingsFields(); _ = _vm.RefreshGitHubAuthAsync(); _vm.RefreshSteamAccount(); }
    }

    // --- Dashboard shortcut handlers --------------------------------------

    /// <summary>"Edit mods" shortcut → open the active server's editor on the Mods tab.</summary>
    private void OnEditMods(object sender, RoutedEventArgs e) => OpenServerEditor(1);

    /// <summary>"Edit params" shortcut → open the active server's editor on the Params tab.</summary>
    private void OnEditParams(object sender, RoutedEventArgs e) => OpenServerEditor(2);

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
        CfgAutomountWorkDrive.IsChecked = c.AutomountWorkDrive;
        CfgWorkDriveSource.Text = c.WorkDriveSource;
        CfgSigningKey.Text = c.SigningKey;
        CfgKeysDir.Text = c.KeysDir;
        CfgEditorPath.Text = c.EditorPath;
        CfgSteamCmdPath.Text = c.SteamCmdPath;
        CfgSteamLogin.Text = c.SteamLogin;
        SteamSignInStatus.Text = _vm.SteamSignedIn ? "✓ Subscribe works in-app" : "not signed in — Subscribe opens the Steam page";
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
            AutomountWorkDrive = CfgAutomountWorkDrive.IsChecked == true,
            WorkDriveSource = CfgWorkDriveSource.Text.Trim(),
            SigningKey = CfgSigningKey.Text.Trim(),
            KeysDir = CfgKeysDir.Text.Trim(),
            EditorPath = CfgEditorPath.Text.Trim(),
            SteamCmdPath = CfgSteamCmdPath.Text.Trim(),
            SteamLogin = CfgSteamLogin.Text.Trim(),
        };
        _vm.ApplyConfig(edited);
        LoadSettingsFields();
    }

    // === SERVERS page: open the per-server modal editor ===================

    /// <summary>Open the modal editor for the active server on a given tab (0=Settings,1=Mods,2=Params).</summary>
    private void OpenServerEditor(int tab)
    {
        var dlg = new ServerEditorWindow(_vm, tab) { Owner = this };
        dlg.ShowDialog();
        _vm.RefreshServers();   // name/active may have changed (rename/clone)
    }

    /// <summary>Servers row "Settings"/"Mods": activate the clicked server, then open its modal editor.</summary>
    private void OpenServerForRow(object sender, int tab)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        _vm.UseServer(name);
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

    private void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var r = System.Windows.MessageBox.Show(
            $"Delete server \"{name}\"?\n\n" +
            "YES — delete the server AND all its files (serverDZ.cfg, mpmissions, profiles / logs). Cannot be undone.\n\n" +
            "NO — remove it from dzl only; keep the folder + files on disk.\n\n" +
            "CANCEL — don't delete.",
            "Delete server", System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Cancel) return;
        NewServerStatus.Text = _vm.DeleteServer(name, removeFiles: r == System.Windows.MessageBoxResult.Yes);
    }

    private void OnWipeServerPersistence(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        var ok = System.Windows.MessageBox.Show(
            $"Wipe persistence for this server?\n\n{dir}\n\nThe world / loot / player state resets; DayZ " +
            "regenerates fresh Central Economy storage on the next start. The mission files are kept.",
            "Wipe persistence", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        NewServerStatus.Text = _vm.WipePersistenceDir(dir);
    }

    private void OnAddScanRoot(object sender, RoutedEventArgs e)
    {
        var dir = PickFolder();
        if (dir is null) return;
        var existing = CfgScanRoots.Text.TrimEnd();
        CfgScanRoots.Text = existing.Length == 0 ? dir : existing + "\n" + dir;
    }

    // (Per-server settings, mission/config pickers and the launch-params editor now live in
    //  ServerEditorWindow — opened from the Servers page. Removed from the main window.)

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
        try { NewModStatus.Text = _vm.CreateModProject(name, author, NewModInitGit.IsChecked == true); }
        finally { NewModButton.IsEnabled = true; }
        if (NewModStatus.Text.StartsWith('✓')) NewModNameBox.Text = "";
    }

    // Open the project's git remote in the browser (button disabled when there's no remote).
    private void OnOpenRepoUrl(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    // Open the per-mod git client window.
    private void OnOpenGit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        // No Owner: an owned WPF-UI FluentWindow (Mica) can minimize/hide its owner when closed. As an
        // independent top-level tool window, closing it can't touch the main window.
        new GitWindow(_vm, name, _vm.ModDirOf(name)).Show();
    }

    private void OnImportFromGitHub(object sender, RoutedEventArgs e)
    {
        var repo = GhRepoBox.Text.Trim();
        if (repo.Length == 0) { GhImportStatus.Text = "Enter a GitHub repo (owner/name or URL)."; return; }
        GhImportStatus.Text = "cloning…";
        GhImportStatus.Text = _vm.ImportFromGitHub(repo, GhNameBox.Text);
        if (GhImportStatus.Text.StartsWith('✓')) { GhRepoBox.Text = ""; GhNameBox.Text = ""; }
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

    // Capture the app (main window + any open child windows) to a PNG under the screenshots folder.
    private async void OnScreenshot(object sender, RoutedEventArgs e)
    {
        ScreenshotStatus.Text = "capturing…";
        await System.Threading.Tasks.Task.Delay(150);   // let the button's pressed state clear before the grab
        try
        {
            var path = AppScreenshot.Capture(App.ConfigPath());
            ScreenshotStatus.Text = $"✓ saved {System.IO.Path.GetFileName(path)}";
            ScreenshotStatus.ToolTip = path;
        }
        catch (Exception ex) { ScreenshotStatus.Text = "✗ " + ex.Message; }
    }

    // Open the per-module settings modal (⚙ on a page). Phase 1: "mods".
    private void OnModuleSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string module }) return;
        new ModuleSettingsWindow(_vm, module) { Owner = this }.ShowDialog();
        LoadSettingsFields();   // keep the global Settings page in sync if it was already populated
    }

    private void OnOpenModFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}\n\n(missing, or the P: link is broken — try Rescan / re-link)",
                "Open mod folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnUnlinkMod(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            NewModStatus.Text = _vm.UnlinkMod(name);
    }

    // Delete a mod project (destructive). Yes = source + build, No = source only, Cancel = abort.
    private void OnDeleteProject(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var r = System.Windows.MessageBox.Show(
            $"Delete project \"{name}\"?\n\nThis removes its P: link and deletes the source folder — this can't be undone.\n\n" +
            "Yes  → also delete the built @" + name + " output\n" +
            "No   → delete the source only\n" +
            "Cancel → keep everything",
            "Delete project", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Cancel) return;
        NewModStatus.Text = _vm.DeleteModProject(name, alsoBuild: r == System.Windows.MessageBoxResult.Yes);
    }

    private async void OnBuildMod(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var opt = BuildDialog.Show(this, _vm, name);     // resolves the plan + pre-fills paths; null = cancelled
        if (opt is null) return;
        await _vm.BuildModAsync(name, opt.Value.clean, opt.Value.binarize, opt.Value.sign);
    }


    // GitHub OAuth login is interactive (device code + browser), so run it in a real terminal the
    // user can complete; afterwards they can re-open Settings to see the refreshed account.
    private void OnGitHubLogin(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k gh auth login --web --hostname github.com --git-protocol https")
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Could not launch gh login:\n" + ex.Message, "GitHub login",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private async void OnGitHubLogout(object sender, RoutedEventArgs e) => await _vm.GitHubLogoutAsync();

    // Persist the typed signing-key name + keys folder first (so generation uses them), then run DSCreateKey.
    private void OnGenerateKey(object sender, RoutedEventArgs e)
    {
        _vm.ApplyConfig(_vm.Cfg with { SigningKey = CfgSigningKey.Text.Trim(), KeysDir = CfgKeysDir.Text.Trim() });
        SigningStatus.Text = _vm.GenerateSigningKey();
    }

    // === Steam Workshop ===================================================

    private void OnOpenWorkshop(object sender, RoutedEventArgs e)
        => new WorkshopWindow(_vm).Show();   // no Owner — see OnOpenGit (owned FluentWindow can hide its owner)

    // === Economy (types.xml) editor =======================================

    // Batch + remove operate on the CHECKED rows (checkbox column), NOT the grid's focused/edited row.
    // The detail form edits the single focused row (grid SelectedItem) — selection-for-edit and
    // selection-for-batch are separate concepts now.
    private System.Collections.Generic.List<TypeRowVm> CheckedTypes() =>
        _vm.TypesEditor.CheckedTypes.ToList();

    private void OnReloadTypes(object sender, RoutedEventArgs e) { _vm.TypesEditor.LoadTypes(); RefreshTypesBackupsMenu(); }
    private void OnSaveTypes(object sender, RoutedEventArgs e) { _vm.TypesEditor.SaveTypes(); RefreshTypesBackupsMenu(); }

    /// <summary>Reload the Dictionaries data when the user switches to the Dictionaries sub-tab of the
    /// Economy tab shell, so stale limits (e.g. after types/limits edits) are refreshed immediately.
    /// Guarded against child SelectionChanged bubbling by checking e.OriginalSource is this TabControl.</summary>
    private void OnEconomyTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only react to selection changes on the exact Economy TabControl, not bubbled events from
        // child controls (DataGrid, ComboBox, etc.) whose SelectionChanged also bubbles up.
        if (!ReferenceEquals(e.OriginalSource, EconomyTabControl)) return;
        if (EconomyTabControl.SelectedItem is TabItem { Header: "Dictionaries" })
            _vm.RefreshDictionaries();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Random Presets" })
            _vm.RefreshRandomPresets();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Spawnable Types" })
            _vm.RefreshSpawnableTypes();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Globals" })
            _vm.RefreshGlobals();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Events" })
            _vm.RefreshEvents();
        else if (EconomyTabControl.SelectedItem is TabItem { Header: "Player Spawns" })
            _vm.RefreshPlayerSpawns();
    }

    private void OnAddType(object sender, RoutedEventArgs e)
    {
        var result = NewTypeDialog.Show(this, _vm.TypesEditor.TypesSourceFiles());
        if (result is { } r) _vm.TypesEditor.AddType(r.name, r.targetFile);
    }

    private void OnDuplicateType(object sender, RoutedEventArgs e)
    {
        if (_vm.TypesEditor.SelectedType is not { } src)
        { System.Windows.MessageBox.Show("Select a row to duplicate first.", "Duplicate", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        var result = NewTypeDialog.Show(this, _vm.TypesEditor.TypesSourceFiles(),
            title: "Duplicate type", defaultName: src.Name + "_Copy", okLabel: "Duplicate");
        if (result is { } r) _vm.TypesEditor.DuplicateType(src, r.name, r.targetFile);
    }

    // Remove acts on the checked rows; falls back to the single focused row when nothing is checked.
    private void OnRemoveTypes(object sender, RoutedEventArgs e)
    {
        var rows = CheckedTypes();
        if (rows.Count == 0 && TypesGrid.SelectedItem is TypeRowVm sel) rows = new() { sel };
        _vm.TypesEditor.RemoveTypes(rows);
    }

    private void OnClearFilters(object sender, RoutedEventArgs e) => _vm.TypesEditor.ClearTypeFilters();

    // Push the grid's focused row into the VM (drives the detail form). Batch selection is the checkbox set.
    private void OnTypesSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => _vm.TypesEditor.SetSelectedTypes(
            TypesGrid.SelectedItem is TypeRowVm row ? new[] { row } : System.Array.Empty<TypeRowVm>(),
            TypesGrid.SelectedItem as TypeRowVm);

    // Jump to (and select) the first row with lint findings.
    private void OnJumpToLint(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var hit = _vm.TypesEditor.TypesView.Cast<TypeRowVm>().FirstOrDefault(r => r.HasLint);
        if (hit is null) return;
        TypesGrid.SelectedItem = hit;
        TypesGrid.ScrollIntoView(hit);
    }

    private void OnBatchSet(object sender, RoutedEventArgs e) => Batch(multiply: false);
    private void OnBatchMultiply(object sender, RoutedEventArgs e) => Batch(multiply: true);

    private void Batch(bool multiply)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var field = (BatchFieldBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "nominal";
        if (!double.TryParse(BatchValueBox.Text.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        { System.Windows.MessageBox.Show("Enter a numeric value.", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        _vm.TypesEditor.BatchApply(rows, field, val, multiply);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchFlagSet(object sender, RoutedEventArgs e) => BatchFlag("set");
    private void OnBatchFlagClear(object sender, RoutedEventArgs e) => BatchFlag("clear");
    private void OnBatchFlagToggle(object sender, RoutedEventArgs e) => BatchFlag("toggle");

    private void BatchFlag(string op)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var flag = (BatchFlagBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "map";
        _vm.TypesEditor.BatchFlag(rows, flag, op);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchListAdd(object sender, RoutedEventArgs e) => BatchList(add: true);
    private void OnBatchListRemove(object sender, RoutedEventArgs e) => BatchList(add: false);

    private void BatchList(bool add)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var list = (BatchListBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "usage";
        var val = BatchListValueBox.Text.Trim();
        if (val.Length == 0) { System.Windows.MessageBox.Show("Enter a list value.", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); return; }
        _vm.TypesEditor.BatchList(rows, list, val, add);
        TypesGrid.Items.Refresh();
    }

    private void OnBatchCategorySet(object sender, RoutedEventArgs e)
    {
        var rows = CheckedTypes();
        if (!RequireSelection(rows)) return;
        var cat = BatchCategoryBox.Text?.Trim() ?? "";
        _vm.TypesEditor.BatchCategory(rows, cat);
        TypesGrid.Items.Refresh();
    }

    private bool RequireSelection(System.Collections.Generic.IReadOnlyList<TypeRowVm> rows)
    {
        if (rows.Count > 0) return true;
        System.Windows.MessageBox.Show("Check one or more rows first (the checkbox column).", "Batch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        return false;
    }

    // Undo granularity for in-grid cell edits: snapshot the pre-edit state, commit it on edit-commit.
    private void OnTypesBeginEdit(object sender, DataGridBeginningEditEventArgs e) => _vm.TypesEditor.BeginTypeEdit();
    private void OnTypesCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit) _vm.TypesEditor.CommitTypeEdit();
        else _vm.TypesEditor.CancelTypeEdit();
    }

    private void RefreshTypesBackupsMenu()
    {
        TypesBackupsMenu.Items.Clear();
        var backups = _vm.TypesEditor.TypesBackups();
        if (backups.Count == 0)
        {
            TypesBackupsMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(no backups yet)", IsEnabled = false });
            return;
        }
        foreach (var b in backups)
        {
            var item = new System.Windows.Controls.MenuItem { Header = b.Stamp, Tag = b.Path };
            item.Click += OnRestoreTypeBackup;
            TypesBackupsMenu.Items.Add(item);
        }
    }

    private void OnRestoreTypeBackup(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path }) return;
        var ok = System.Windows.MessageBox.Show(
            $"Restore types.xml from backup {System.IO.Path.GetFileName(path)}?\n\nThe current file is snapshotted first (undoable).",
            "Restore backup", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.OK;
        if (!ok) return;
        _vm.TypesEditor.RestoreTypes(path);
        RefreshTypesBackupsMenu();
    }

    // Open a clickable hyperlink (apikey page, etc.) in the default browser.
    private void OnNavigateLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }

    private async void OnInstallSteamCmd(object sender, RoutedEventArgs e)
    {
        var btn = sender as System.Windows.Controls.Control;
        if (btn is not null) btn.IsEnabled = false;
        SteamCmdStatus.Text = "Downloading steamcmd…";
        var (ok, path, msg) = await _vm.InstallSteamCmdAsync();
        if (ok) CfgSteamCmdPath.Text = path;
        SteamCmdStatus.Text = (ok ? "✓ " : "✗ ") + msg + (ok ? "  (Save to apply)" : "");
        if (btn is not null) btn.IsEnabled = true;
    }

    private void OnSteamSignIn(object sender, RoutedEventArgs e)
    {
        var dlg = new SteamLoginWindow(_vm) { Owner = this };
        dlg.ShowDialog();
        _vm.RefreshSteamAccount();
        LoadSettingsFields();   // sign-in auto-fills SteamLogin → reflect it in the steamcmd field
        SteamSignInStatus.Text = _vm.SteamSignedIn ? "✓ Subscribe works in-app" : "not signed in — Subscribe opens the Steam page";
    }

    private void OnSteamSignOut(object sender, RoutedEventArgs e)
    {
        _vm.SteamSignOut();
        _vm.RefreshSteamAccount();
        SteamSignInStatus.Text = "signed out";
    }

    private void OnDetectEditor(object sender, RoutedEventArgs e)
    {
        var editors = _vm.DetectEditors();
        if (editors.Count == 0) { EditorStatus.Text = "No editor found on PATH (cursor/code/…). Browse to one manually."; return; }
        CfgEditorPath.Text = editors[0].Path;   // best match (Cursor first)
        EditorStatus.Text = $"Found: {string.Join(", ", editors.Select(x => x.Name))}. Using {editors[0].Name}. Save to apply.";
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder }) return;
        var msg = _vm.OpenInEditor(folder);
        if (msg.StartsWith('✗'))
            System.Windows.MessageBox.Show(msg.TrimStart('✗', ' '), "Open in editor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // Fill the DayZ + Tools path fields from the Steam libraries (same detection the setup wizard runs);
    // only overwrites a field when detection actually finds something, so manual entries survive a miss.
    private void OnDetectPaths(object sender, RoutedEventArgs e)
    {
        var d = Dzl.Core.Env.EnvDetect.Detect();
        if (!string.IsNullOrWhiteSpace(d.DayzPath)) CfgDayzPath.Text = d.DayzPath;
        if (!string.IsNullOrWhiteSpace(d.ToolsPath)) CfgDayzToolsPath.Text = d.ToolsPath;
        var found = (d.DayzPath is not null ? 1 : 0) + (d.ToolsPath is not null ? 1 : 0);
        System.Windows.MessageBox.Show(
            found == 0 ? "Couldn't find DayZ or DayZ Tools in your Steam libraries — set the paths manually."
                       : $"Filled {found} path(s) from Steam. Review, then Save.",
            "Auto-detect paths", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // === SERVERS page =====================================================

    private void OnRefreshServers(object sender, RoutedEventArgs e) => _vm.RefreshServers();

    private void OnCreateServer(object sender, RoutedEventArgs e)
    {
        var name = NewServerNameBox.Text.Trim();
        if (name.Length == 0) { NewServerStatus.Text = "Enter an instance name."; return; }
        var map = (NewServerMapBox.SelectedItem as string) ?? "chernarus";
        int? port = int.TryParse(NewServerPortBox.Text.Trim(), out var p) ? p : null;
        var baseSel = NewServerBaseBox.SelectedItem as string;
        var baseName = (string.IsNullOrEmpty(baseSel) || baseSel == MainViewModel.VanillaChoice) ? null : baseSel;
        NewServerButton.IsEnabled = false;
        try { NewServerStatus.Text = _vm.CreateServer(name, map, port, baseName); }
        finally { NewServerButton.IsEnabled = true; }
        if (NewServerStatus.Text.StartsWith('✓')) { NewServerNameBox.Text = ""; NewServerPortBox.Text = ""; }
    }

    private void OnUseServer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
            NewServerStatus.Text = _vm.UseServer(name);
    }

    // A base fixes its own map (baked into its serverDZ.cfg + mpmission). When one is
    // selected, lock the map dropdown and reflect the base's map; only vanilla is free to pick.
    private void OnNewServerBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NewServerMapBox is null) return;   // fires once during InitializeComponent before peers exist
        var sel = NewServerBaseBox.SelectedItem as string;
        var vanilla = string.IsNullOrEmpty(sel) || sel == MainViewModel.VanillaChoice;
        NewServerMapBox.IsEnabled = vanilla;
        if (!vanilla)
        {
            var b = _vm.Bases.FirstOrDefault(x => x.Name == sel);
            if (b is not null) NewServerMapBox.SelectedItem = MapAliases.MapName(b.Mission);
        }
    }

    // === Bases (templates) ================================================
    private void OnCreateBaseFromInstall(object sender, RoutedEventArgs e)
    {
        var name = NewBaseNameBox.Text.Trim();
        if (name.Length == 0) { NewBaseStatus.Text = "Enter a base name."; return; }
        var map = (NewBaseMapBox.SelectedItem as string) ?? "chernarus";
        NewBaseStatus.Text = _vm.CreateBaseFromInstall(name, map);
        if (NewBaseStatus.Text.StartsWith('✓')) NewBaseNameBox.Text = "";
    }

    private void OnCreateEmptyBase(object sender, RoutedEventArgs e)
    {
        var name = NewBaseNameBox.Text.Trim();
        if (name.Length == 0) { NewBaseStatus.Text = "Enter a base name."; return; }
        NewBaseStatus.Text = _vm.CreateEmptyBase(name);
        if (NewBaseStatus.Text.StartsWith('✓')) NewBaseNameBox.Text = "";
    }

    private void OnDeleteBase(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var ok = System.Windows.MessageBox.Show(
            $"Delete base \"{name}\"?\n\nThis removes the template folder and all its files. " +
            "Existing instances created from it are not affected.",
            "Delete base", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        NewBaseStatus.Text = _vm.DeleteBase(name);
    }

    private void OnOpenBaseFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var dir = _vm.BaseDirOf(name);
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}", "Open base folder",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnRefreshBases(object sender, RoutedEventArgs e) => _vm.RefreshBases();

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
        var picked = parts[0] == "file" ? PickFile() : PickFolder();
        if (picked is null) return;
        if (FindName(parts[1]) is TextBox tb) tb.Text = picked;
    }

    private static string? PickFile()
    {
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
