using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Servers;
using Dzl.Core.Tools;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;
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
            PageLogs.UpdateLogListRowHeights();
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

            // "Economy" opens its own modeless window (more room; works alongside the main
            // window) — like Setup, it's not an in-app page, so bounce the rail selection back.
            if (tag == "economy")
            {
                OpenEconomyWindow();
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
        PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PageMcp.Visibility = tag == "mcp" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Refresh page-local state on show.
        if (tag == "logs") PageLogs.UpdateLogListRowHeights();
        if (tag == "tools") PageTools.RefreshToolsPage();
        if (tag == "mymods") PageMyMods.RefreshOnShow();
        if (tag == "servers") { _vm.RefreshServers(); _vm.RefreshBases(); }   // base dropdown needs bases
        if (tag == "bases") _vm.RefreshBases();
        if (tag == "settings") { LoadSettingsFields(); _ = _vm.RefreshGitHubAuthAsync(); _vm.RefreshSteamAccount(); }
    }

    // --- Economy window (modeless, single instance) ------------------------

    private EconomyWindow? _economyWin;

    /// <summary>Open (or focus) the Central Economy editor window. Modeless and ownerless on
    /// purpose: an owned Mica FluentWindow hides its owner when closed, and the editor must not
    /// block the main window. The shared MainViewModel keeps all editor state across closes.</summary>
    private void OpenEconomyWindow()
    {
        if (_economyWin is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return;
        }
        _economyWin = new EconomyWindow(_vm);
        _economyWin.Closed += (_, _) => _economyWin = null;
        _economyWin.Show();
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

    // The Logs page (auto-scroll, list-view row heights, open-folder/clear) now lives in
    // Views/LogsView. ShowPage("logs") seeds its row heights via PageLogs.UpdateLogListRowHeights().

    // === Work drive (Settings page + bottom status bar) ===================

    // The Tools page owns its own copies of these (ToolsView); these stay here for the Settings
    // page's "Work drive (P:)" card and the app-wide bottom status bar's "Mount P:" quick button.
    private void OnMountWorkDrive(object sender, RoutedEventArgs e) => _vm.MountWorkDrive();
    private void OnUnmountWorkDrive(object sender, RoutedEventArgs e) => _vm.UnmountWorkDrive();

    // === SETTINGS page ====================================================

    /// <summary>Re-read the global Settings page from the live config. Called by the Mods / My Mods
    /// views after the per-module settings modal closes, so the Settings page mirrors any config
    /// the module edited (the pages are never visible at once, but this keeps state consistent).</summary>
    internal void SyncSettingsPage() => LoadSettingsFields();

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
        // Keys dropdown: whatever exists in the keys folder, with the effective name selected —
        // blank config falls back to the cached author handle; saving makes the choice explicit.
        CfgSigningKey.ItemsSource = _vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = !string.IsNullOrWhiteSpace(c.SigningKey) ? c.SigningKey : _vm.CachedAuthor;
        CfgKeysDir.Text = c.KeysDir;
        RefreshSigningStatus();
        CfgEditorPath.ItemsSource = _vm.DetectEditors();   // dropdown of detected editors; text stays the saved value
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

    // (The Servers page — create/use/delete/wipe instances + open the per-server modal editor —
    //  now lives in Views/ServersView.)

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

    // (The My Mods page — create/import/clone mod projects + per-project Build/Git/open/link/
    //  delete, and the per-module settings modal — now lives in Views/MyModsView.)

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

    // Shared flow (SigningKeysUi): prompt for a NEW name, refuse taken ones, run DSCreateKey,
    // then refresh the dropdown so the new key appears.
    private void OnGenerateKey(object sender, RoutedEventArgs e)
    {
        var (name, status) = SigningKeysUi.GenerateInteractive(this, _vm, CfgKeysDir.Text);
        if (name is null) return;
        SigningStatus.Text = status;
        CfgSigningKey.ItemsSource = _vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = name;
    }

    /// <summary>Live key state for the Settings page: ✓ ready / not created yet, for the
    /// effective name + folder currently in the boxes (not yet necessarily saved).</summary>
    private void RefreshSigningStatus() =>
        SigningStatus.Text = SigningKeysUi.Status(_vm, CfgSigningKey.Text);

    // Shared flow (SigningKeysUi): copy an existing .biprivatekey (+ .bikey) into the keys folder.
    private void OnImportKeys(object sender, RoutedEventArgs e)
    {
        var (name, status) = SigningKeysUi.ImportInteractive(this, _vm, CfgKeysDir.Text);
        if (name is null) { if (status.Length > 0) SigningStatus.Text = status; return; }
        CfgSigningKey.ItemsSource = _vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = name;
        SigningStatus.Text = status;
    }

    // === Steam Workshop ===================================================

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
        CfgEditorPath.ItemsSource = editors;
        if (editors.Count == 0) { EditorStatus.Text = "No editor found on PATH (cursor/code/…). Browse to one manually."; return; }
        CfgEditorPath.Text = editors[0].Path;   // best match (Cursor first); the dropdown holds the rest
        EditorStatus.Text = $"Found {editors.Count}: {string.Join(", ", editors.Select(x => x.Name))} — pick from the dropdown. Save to apply.";
    }

    // Selecting a detected editor from the dropdown puts its PATH into the editable text — the
    // combo would otherwise render the record's ToString(). Deferred: the combo overwrites Text
    // right after SelectionChanged.
    private void OnEditorPicked(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CfgEditorPath.SelectedItem is not Dzl.Core.Env.EditorInfo info) return;
        Dispatcher.BeginInvoke(() => CfgEditorPath.Text = info.Path);
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
        else if (FindName(parts[1]) is System.Windows.Controls.ComboBox cb) cb.Text = picked;
    }

    private static string? PickFile()
    {
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
