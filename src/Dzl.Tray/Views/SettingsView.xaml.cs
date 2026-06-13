using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray.Views;

/// <summary>Settings page: machine-global config (accounts, paths, exes, scan-roots, automation,
/// work drive, signing, editor, steamcmd). The named Cfg* controls are bulk-read into the live
/// config on Save and bulk-populated by <see cref="Reload"/>. All state lives on
/// <see cref="MainViewModel"/> (the inherited DataContext).</summary>
public partial class SettingsView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public SettingsView() => InitializeComponent();

    /// <summary>Populate every field from the live config. Public so the host window can call it
    /// when the Settings page is shown, after the setup wizard finishes, and after a per-module
    /// settings modal closes.</summary>
    public void Reload()
    {
        if (Vm is null) return;
        var c = Vm.Cfg;
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
        CfgSigningKey.ItemsSource = Vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = !string.IsNullOrWhiteSpace(c.SigningKey) ? c.SigningKey : Vm.CachedAuthor;
        CfgKeysDir.Text = c.KeysDir;
        RefreshSigningStatus();
        CfgEditorPath.ItemsSource = Vm.DetectEditors();   // dropdown of detected editors; text stays the saved value
        CfgEditorPath.Text = c.EditorPath;
        CfgSteamCmdPath.Text = c.SteamCmdPath;
        CfgSteamLogin.Text = c.SteamLogin;
        SteamSignInStatus.Text = Vm.SteamSignedIn ? "✓ Subscribe works in-app" : "not signed in — Subscribe opens the Steam page";
        ConfigError.Visibility = Visibility.Collapsed;
    }

    private void OnRevertConfig(object sender, RoutedEventArgs e) => Reload();

    private void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        ConfigError.Visibility = Visibility.Collapsed;
        var roots = CfgScanRoots.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        var edited = Vm.Cfg with
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
        Vm.ApplyConfig(edited);
        Reload();
    }

    private void OnAddScanRoot(object sender, RoutedEventArgs e)
    {
        var dir = PickFolder();
        if (dir is null) return;
        var existing = CfgScanRoots.Text.TrimEnd();
        CfgScanRoots.Text = existing.Length == 0 ? dir : existing + "\n" + dir;
    }

    /// <summary>Re-open the environment setup wizard (Settings button entry point). The wizard
    /// reloads the VM + this page on Finish; that flow lives on the host window because the nav
    /// rail's "Setup" item shares it.</summary>
    private void OnRunSetupWizard(object sender, RoutedEventArgs e)
        => (Window.GetWindow(this) as MainWindow)?.OpenSetupWizard();

    // === Work drive (Settings card) — the host bottom status bar + Tools page own their copies ===
    private void OnMountWorkDrive(object sender, RoutedEventArgs e) => Vm?.MountWorkDrive();
    private void OnUnmountWorkDrive(object sender, RoutedEventArgs e) => Vm?.UnmountWorkDrive();

    // === Accounts ===========================================================

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

    private async void OnGitHubLogout(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.GitHubLogoutAsync();
    }

    private void OnSteamSignIn(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new SteamLoginWindow(Vm) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        Vm.RefreshSteamAccount();
        Reload();   // sign-in auto-fills SteamLogin → reflect it in the steamcmd field
        SteamSignInStatus.Text = Vm.SteamSignedIn ? "✓ Subscribe works in-app" : "not signed in — Subscribe opens the Steam page";
    }

    private void OnSteamSignOut(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.SteamSignOut();
        Vm.RefreshSteamAccount();
        SteamSignInStatus.Text = "signed out";
    }

    // === Signing ============================================================

    // Shared flow (SigningKeysUi): prompt for a NEW name, refuse taken ones, run DSCreateKey,
    // then refresh the dropdown so the new key appears.
    private void OnGenerateKey(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var (name, status) = SigningKeysUi.GenerateInteractive(Window.GetWindow(this), Vm, CfgKeysDir.Text);
        if (name is null) return;
        SigningStatus.Text = status;
        CfgSigningKey.ItemsSource = Vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = name;
    }

    /// <summary>Live key state for the Settings page: ✓ ready / not created yet, for the
    /// effective name + folder currently in the boxes (not yet necessarily saved).</summary>
    private void RefreshSigningStatus()
    {
        if (Vm is not null) SigningStatus.Text = SigningKeysUi.Status(Vm, CfgSigningKey.Text);
    }

    // Shared flow (SigningKeysUi): copy an existing .biprivatekey (+ .bikey) into the keys folder.
    private void OnImportKeys(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var (name, status) = SigningKeysUi.ImportInteractive(Window.GetWindow(this), Vm, CfgKeysDir.Text);
        if (name is null) { if (status.Length > 0) SigningStatus.Text = status; return; }
        CfgSigningKey.ItemsSource = Vm.ListSigningKeys().Select(k => k.Name).ToList();
        CfgSigningKey.Text = name;
        SigningStatus.Text = status;
    }

    // === steamcmd ===========================================================

    private async void OnInstallSteamCmd(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var btn = sender as System.Windows.Controls.Control;
        if (btn is not null) btn.IsEnabled = false;
        SteamCmdStatus.Text = "Downloading steamcmd…";
        var (ok, path, msg) = await Vm.InstallSteamCmdAsync();
        if (ok) CfgSteamCmdPath.Text = path;
        SteamCmdStatus.Text = (ok ? "✓ " : "✗ ") + msg + (ok ? "  (Save to apply)" : "");
        if (btn is not null) btn.IsEnabled = true;
    }

    // === Editor =============================================================

    private void OnDetectEditor(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var editors = Vm.DetectEditors();
        CfgEditorPath.ItemsSource = editors;
        if (editors.Count == 0) { EditorStatus.Text = "No editor found on PATH (cursor/code/…). Browse to one manually."; return; }
        CfgEditorPath.Text = editors[0].Path;   // best match (Cursor first); the dropdown holds the rest
        EditorStatus.Text = $"Found {editors.Count}: {string.Join(", ", editors.Select(x => x.Name))} — pick from the dropdown. Save to apply.";
    }

    // Selecting a detected editor from the dropdown puts its PATH into the editable text — the
    // combo would otherwise render the record's ToString(). Deferred: the combo overwrites Text
    // right after SelectionChanged.
    private void OnEditorPicked(object sender, SelectionChangedEventArgs e)
    {
        if (CfgEditorPath.SelectedItem is not Dzl.Core.Env.EditorInfo info) return;
        Dispatcher.BeginInvoke(() => CfgEditorPath.Text = info.Path);
    }

    // === Paths ==============================================================

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

    // === Shared pickers / folder open =======================================

    /// <summary>Browse into a named TextBox/ComboBox on this page. Tag form: "dir:&lt;Field&gt;" or
    /// "file:&lt;Field&gt;".</summary>
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

    /// <summary>Show a folder picker (OpenFolderDialog on .NET 8 WPF); null if cancelled.</summary>
    private string? PickFolder()
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FolderName : null;
    }

    private static string? PickFile()
    {
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
