using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Mods;
using Dzl.Core.Tools;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Dzl.Tray;

/// <summary>
/// First-run (and on-demand) setup wizard: walks the user from a fresh machine to a working
/// local DayZ dev environment — confirm DayZ + Tools paths, mount the P: work drive, extract
/// vanilla game data, scaffold a server instance, optionally fetch the dedicated server via
/// SteamCMD, and set mod scan-roots — then writes a <see cref="DzlConfig"/>.
///
/// Self-contained code-behind: an <c>_step</c> index drives which of eight content panels is
/// visible plus the left step indicator; the bottom bar (Back/Next/Finish/Cancel) advances it.
/// </summary>
public partial class SetupWizardWindow : FluentWindow
{
    private const int LastStep = 7;
    private readonly string _configPath;
    private int _step;

    private readonly Border[] _stepRows;
    private readonly UIElement[] _pages;

    public SetupWizardWindow(string configPath)
    {
        InitializeComponent();
        _configPath = configPath;

        _stepRows = new[] { StepRow0, StepRow1, StepRow2, StepRow3, StepRow4, StepRow5, StepRow6, StepRow7 };
        _pages = new UIElement[] { Page0, Page1, Page2, Page3, Page4, Page5, Page6, Page7 };

        Loaded += (_, _) => ShowStep(0);
    }

    // ---- Step navigation ------------------------------------------------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, LastStep);

        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < _stepRows.Length; i++)
            _stepRows[i].Background = i == _step
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x2D, 0x7D, 0xD2))
                : System.Windows.Media.Brushes.Transparent;

        // Bottom-bar state.
        BackBtn.IsEnabled = _step > 0;
        bool onLast = _step == LastStep;
        NextBtn.Visibility = onLast ? Visibility.Collapsed : Visibility.Visible;
        FinishBtn.Visibility = onLast ? Visibility.Visible : Visibility.Collapsed;

        OnEnterStep(_step);
        UpdateNextEnabled();
    }

    /// <summary>Per-step entry logic (detection, prefill, status refresh).</summary>
    private void OnEnterStep(int step)
    {
        switch (step)
        {
            case 1: // Paths — detect + prefill (only if still blank, so re-entry keeps edits)
                if (string.IsNullOrWhiteSpace(DayzPathBox.Text)
                    && string.IsNullOrWhiteSpace(ToolsPathBox.Text)
                    && string.IsNullOrWhiteSpace(ServerPathBox.Text))
                {
                    var d = EnvDetect.Detect();
                    DayzPathBox.Text = d.DayzPath ?? "";
                    ToolsPathBox.Text = d.ToolsPath ?? "";
                    ServerPathBox.Text = d.ServerPath ?? "";
                }
                RefreshPathStatuses();
                break;

            case 2: // Work drive
                RefreshWorkDrive();
                break;

            case 3: // Game data
                GameDataNote.Text = string.IsNullOrWhiteSpace(ToolsPathBox.Text)
                    ? "DayZ Tools path not set (step 2) — set it to open the tools from here."
                    : "Vanilla data extracts to P:\\ (mount it in step 3 first).";
                break;

            case 4: // Server instance — default instance dir = DayZ path
                if (string.IsNullOrWhiteSpace(InstanceDirBox.Text))
                    InstanceDirBox.Text = DayzPathBox.Text;
                break;

            case 5: // Server files — steamcmd line
                SteamCmdBox.Text = SteamCmd.DownloadServerScript(ServerDirForSteamCmd());
                break;

            case 6: // Mods — prefill scan roots
                if (string.IsNullOrWhiteSpace(ScanRootsBox.Text))
                    ScanRootsBox.Text = string.Join("\r\n", DefaultScanRoots());
                break;

            case 7: // Finish — summary
                SummaryBox.Text = BuildSummary();
                break;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(_step - 1);
    private void OnNext(object sender, RoutedEventArgs e) => ShowStep(_step + 1);
    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    /// <summary>Next is gated only on step 2 (Paths): DayZ install must be a real directory.
    /// Every later step is skippable, so Next stays enabled there.</summary>
    private void UpdateNextEnabled()
    {
        NextBtn.IsEnabled = _step != 1 || Directory.Exists(DayzPathBox.Text?.Trim() ?? "");
    }

    // ---- Step 2: Paths --------------------------------------------------

    private void OnDayzPathChanged(object sender, TextChangedEventArgs e)
    {
        if (DayzPathStatus is null) return; // pre-template
        SetFoundStatus(DayzPathStatus, DayzPathBox.Text, required: true);
        UpdateNextEnabled();
    }

    private void OnToolsPathChanged(object sender, TextChangedEventArgs e)
    {
        if (ToolsPathStatus is null) return;
        SetFoundStatus(ToolsPathStatus, ToolsPathBox.Text, required: false);
    }

    private void OnServerPathChanged(object sender, TextChangedEventArgs e)
    {
        if (ServerPathStatus is null) return;
        SetFoundStatus(ServerPathStatus, ServerPathBox.Text, required: false);
    }

    private void RefreshPathStatuses()
    {
        SetFoundStatus(DayzPathStatus, DayzPathBox.Text, required: true);
        SetFoundStatus(ToolsPathStatus, ToolsPathBox.Text, required: false);
        SetFoundStatus(ServerPathStatus, ServerPathBox.Text, required: false);
    }

    private static void SetFoundStatus(TextBlock target, string? path, bool required)
    {
        var p = path?.Trim() ?? "";
        if (Directory.Exists(p))
        {
            target.Text = "✓ found";
            target.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
        }
        else if (p.Length == 0)
        {
            target.Text = required ? "✗ required" : "— not set (optional)";
            target.Foreground = required
                ? System.Windows.Media.Brushes.IndianRed
                : System.Windows.Media.Brushes.Gray;
        }
        else
        {
            target.Text = "✗ folder does not exist";
            target.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    // ---- Step 3: Work drive ---------------------------------------------

    private void RefreshWorkDrive()
    {
        bool mounted = WorkDrive.IsMounted();
        WorkDriveStatus.Text = mounted ? "✓ P: is mounted" : "✗ P: is not mounted";
        WorkDriveStatus.Foreground = mounted
            ? System.Windows.Media.Brushes.MediumSeaGreen
            : System.Windows.Media.Brushes.IndianRed;
        MountBtn.IsEnabled = !mounted;
        WorkDriveNote.Text = string.IsNullOrWhiteSpace(ToolsPathBox.Text)
            ? "Set the DayZ Tools path on the Paths step to enable mounting."
            : @"Mounts via <Tools>\Bin\WorkDrive\WorkDrive.exe.";
    }

    private void OnMountWorkDrive(object sender, RoutedEventArgs e)
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        if (tools.Length == 0)
        {
            WorkDriveNote.Text = "DayZ Tools path is required to mount the work drive.";
            return;
        }
        var exe = Path.Combine(tools, "Bin", "WorkDrive", "WorkDrive.exe");
        WorkDrive.Mount(exe);
        RefreshWorkDrive();
    }

    // ---- Step 4: Game data ----------------------------------------------

    private void OnOpenDayzTools(object sender, RoutedEventArgs e)
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        if (tools.Length == 0)
        {
            GameDataNote.Text = "Set the DayZ Tools path on the Paths step first.";
            return;
        }
        var entry = ToolCatalog.Find(tools, "launcher") ?? ToolCatalog.Find(tools, "workbench");
        bool ok = entry is not null && ToolLauncher.Launch(entry);
        GameDataNote.Text = ok
            ? "DayZ Tools launched. Click 'Extract Game Data' there to unpack vanilla PBOs to P:."
            : "Could not find the DayZ Tools launcher/Workbench under the configured Tools path.";
    }

    // ---- Step 5: Server instance ----------------------------------------

    private void OnScaffold(object sender, RoutedEventArgs e)
    {
        var dayz = DayzPathBox.Text?.Trim() ?? "";
        var instance = InstanceDirBox.Text?.Trim() ?? "";
        if (instance.Length == 0)
        {
            ScaffoldReportBox.Text = "Set an instance directory first.";
            return;
        }
        var r = ServerScaffold.Scaffold(dayz, instance);
        ScaffoldReportBox.Text =
            $"serverDZ.cfg created : {Yn(r.CfgCreated)}\r\n" +
            $"profiles created     : {Yn(r.ProfilesCreated)}\r\n" +
            $"profiles_client made : {Yn(r.ClientProfilesCreated)}\r\n" +
            $"mission copied       : {Yn(r.MissionCopied)}\r\n" +
            (string.IsNullOrEmpty(r.Notes) ? "" : $"\r\nnotes: {r.Notes}");
    }

    private static string Yn(bool b) => b ? "yes" : "no";

    // ---- Step 6: Server files (steamcmd) --------------------------------

    private string ServerDirForSteamCmd()
    {
        var server = ServerPathBox.Text?.Trim() ?? "";
        return server.Length > 0 ? server : @"C:\DayZServer";
    }

    private void OnCopySteamCmd(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(SteamCmdBox.Text); } catch { /* clipboard busy; ignore */ }
    }

    // ---- Step 7: Mods ---------------------------------------------------

    private string[] DefaultScanRoots()
    {
        var roots = new List<string>();
        if (WorkDrive.IsMounted()) roots.Add(@"P:\");
        roots.AddRange(new[] { @"P:\@Dependencies", @"P:\@PackedMods", @"P:\" });
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void OnPreviewMods(object sender, RoutedEventArgs e)
    {
        var roots = ScanRootLines();
        int count = ModDiscovery.Discover(roots).Count;
        ModsPreviewStatus.Text = $"Found {count} mod{(count == 1 ? "" : "s")} across {roots.Count} root{(roots.Count == 1 ? "" : "s")}.";
        ModsPreviewStatus.Foreground = count > 0
            ? System.Windows.Media.Brushes.MediumSeaGreen
            : System.Windows.Media.Brushes.Gray;
    }

    private List<string> ScanRootLines() =>
        (ScanRootsBox.Text ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    // ---- Step 8: Finish -------------------------------------------------

    private string BuildSummary()
    {
        var instance = InstanceDirBox.Text?.Trim() ?? "";
        var roots = ScanRootLines();
        return
            $"DayZ install   : {Show(DayzPathBox.Text)}\r\n" +
            $"DayZ Tools     : {Show(ToolsPathBox.Text)}\r\n" +
            $"DayZ Server    : {Show(ServerPathBox.Text)}\r\n" +
            $"Instance dir   : {Show(instance)}\r\n" +
            $"Profiles       : {(instance.Length > 0 ? Path.Combine(instance, "profiles") : "(default)")}\r\n" +
            $"Client profiles: {(instance.Length > 0 ? Path.Combine(instance, "profiles_client") : "(default)")}\r\n" +
            $"P: mounted     : {(WorkDrive.IsMounted() ? "yes" : "no")}\r\n" +
            $"Scan roots     :\r\n" +
            (roots.Count > 0 ? string.Join("\r\n", roots.Select(r => "  " + r)) : "  (none)");
    }

    private static string Show(string? s) => string.IsNullOrWhiteSpace(s) ? "(not set)" : s.Trim();

    private void OnFinish(object sender, RoutedEventArgs e)
    {
        var defaults = DzlConfig.Default();
        var dayz = Show(DayzPathBox.Text) == "(not set)" ? defaults.DayzPath : DayzPathBox.Text.Trim();
        var tools = Show(ToolsPathBox.Text) == "(not set)" ? defaults.DayzToolsPath : ToolsPathBox.Text.Trim();
        var instance = InstanceDirBox.Text?.Trim() ?? "";
        var roots = ScanRootLines();

        var cfg = defaults with
        {
            DayzPath = dayz,
            DayzToolsPath = tools,
            ProfilesPath = instance.Length > 0 ? Path.Combine(instance, "profiles") : defaults.ProfilesPath,
            ClientProfilesPath = instance.Length > 0 ? Path.Combine(instance, "profiles_client") : defaults.ClientProfilesPath,
            ScanRoots = roots.Count > 0 ? roots : defaults.ScanRoots,
        };

        ConfigStore.Save(cfg, _configPath);
        Profiles.EnsureDefault(_configPath); // seed a default profile holding this config

        DialogResult = true;
        Close();
    }

    // ---- Shared folder picker (Tag = target TextBox x:Name) -------------

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var dlg = new OpenFolderDialog();
        var current = (FindName(name) as TextBox)?.Text?.Trim();
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            dlg.InitialDirectory = current;
        if (dlg.ShowDialog(this) == true && FindName(name) is TextBox tb)
            tb.Text = dlg.FolderName;
    }
}
