using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private const int LastStep = 8;
    // Step 1 (0-based) is the Environment check inserted between Welcome (0) and Paths (2).
    private const int CheckStep = 1;
    private readonly string _configPath;
    private int _step;

    private readonly Border[] _stepRows;
    private readonly UIElement[] _pages;

    public SetupWizardWindow(string configPath)
    {
        InitializeComponent();
        _configPath = configPath;

        // Order matters: the array index IS the step number. Check sits at index 1.
        _stepRows = new[] { StepRow0, StepRowCheck, StepRow1, StepRow2, StepRow3, StepRow4, StepRow5, StepRow6, StepRow7 };
        _pages = new UIElement[] { Page0, PageCheck, Page1, Page2, Page3, Page4, Page5, Page6, Page7 };

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
            case CheckStep: // Environment check — run diagnostics
                RunEnvCheck();
                break;

            case 2: // Paths — detect + prefill (only if still blank, so re-entry keeps edits)
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

            case 3: // Work drive — prefill the work folder (becomes P:)
                if (string.IsNullOrWhiteSpace(WorkFolderBox.Text))
                    WorkFolderBox.Text = DefaultWorkFolder();
                RefreshWorkDrive();
                break;

            case 4: // Game data
                GameDataNote.Text = string.IsNullOrWhiteSpace(ToolsPathBox.Text)
                    ? "DayZ Tools path not set (Paths step) — set it to open the tools from here."
                    : "Vanilla data extracts to P:\\ (mount it on the Work drive step first).";
                break;

            case 5: // Server instance — default instance dir = DayZ path
                if (string.IsNullOrWhiteSpace(InstanceDirBox.Text))
                    InstanceDirBox.Text = DayzPathBox.Text;
                break;

            case 6: // Server files — steamcmd line
                SteamCmdBox.Text = SteamCmd.DownloadServerScript(ServerDirForSteamCmd());
                break;

            case 7: // Mods — prefill scan roots
                if (string.IsNullOrWhiteSpace(ScanRootsBox.Text))
                    ScanRootsBox.Text = string.Join("\r\n", DefaultScanRoots());
                break;

            case 8: // Finish — summary
                SummaryBox.Text = BuildSummary();
                break;
        }
    }

    // ---- Step (Check): Environment check --------------------------------

    /// <summary>One row in the environment-check list: glyph + color + label + detail.</summary>
    public sealed record CheckRow(string Glyph, System.Windows.Media.Brush Brush, string Label, string Detail);

    /// <summary>
    /// Run <see cref="EnvCheck"/> against the wizard's working config and render a ✓/✗/⚠/ℹ
    /// checklist. If the user has edited paths on the Paths step, those win over the saved config
    /// so the doctor reflects what they're about to set.
    /// </summary>
    private void RunEnvCheck()
    {
        var cfg = CurrentWizardConfig();
        var items = EnvCheck.Run(cfg, () => WorkDrive.IsMounted());

        var rows = new List<CheckRow>();
        int passed = 0;
        bool anyError = false;
        foreach (var it in items)
        {
            if (it.Ok) passed++;
            else if (it.Severity == CheckSeverity.Error) anyError = true;

            var (glyph, brush) = GlyphFor(it);
            rows.Add(new CheckRow(glyph, brush, it.Label, it.Detail));
        }

        CheckList.ItemsSource = rows;
        CheckSummary.Text = $"{passed} of {items.Count} checks passed";
        CheckSummary.Foreground = anyError
            ? System.Windows.Media.Brushes.IndianRed
            : (passed == items.Count
                ? System.Windows.Media.Brushes.MediumSeaGreen
                : System.Windows.Media.Brushes.Goldenrod);
    }

    private static (string glyph, System.Windows.Media.Brush brush) GlyphFor(CheckItem it)
    {
        if (it.Ok) return ("✓", System.Windows.Media.Brushes.MediumSeaGreen);
        return it.Severity switch
        {
            CheckSeverity.Error => ("✗", System.Windows.Media.Brushes.IndianRed),
            CheckSeverity.Warning => ("⚠", System.Windows.Media.Brushes.Goldenrod),
            _ => ("ℹ", System.Windows.Media.Brushes.Gray),
        };
    }

    /// <summary>
    /// The config the check runs against: the saved config on disk, but with any path the user
    /// has already typed on the Paths step layered on top (those boxes may be empty before the
    /// user reaches Paths, in which case the saved values are kept).
    /// </summary>
    private DzlConfig CurrentWizardConfig()
    {
        var cfg = ConfigStore.Load(_configPath);
        var dayz = DayzPathBox.Text?.Trim();
        var tools = ToolsPathBox.Text?.Trim();
        if (!string.IsNullOrEmpty(dayz)) cfg = cfg with { DayzPath = dayz };
        if (!string.IsNullOrEmpty(tools)) cfg = cfg with { DayzToolsPath = tools };
        return cfg;
    }

    private void OnReCheck(object sender, RoutedEventArgs e) => RunEnvCheck();

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(_step - 1);
    private void OnNext(object sender, RoutedEventArgs e) => ShowStep(_step + 1);
    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private const int PathsStep = 2;

    /// <summary>Next is gated only on the Paths step: DayZ install must be a real directory.
    /// Every later step is skippable, so Next stays enabled there.</summary>
    private void UpdateNextEnabled()
    {
        NextBtn.IsEnabled = _step != PathsStep || Directory.Exists(DayzPathBox.Text?.Trim() ?? "");
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

    /// <summary>Re-run detection and fill any path Detect now finds, leaving user-typed values
    /// that detection still can't locate untouched (so freshly-installed Tools/Server populate).</summary>
    private void OnReDetect(object sender, RoutedEventArgs e)
    {
        var d = EnvDetect.Detect();
        if (!string.IsNullOrWhiteSpace(d.DayzPath)) DayzPathBox.Text = d.DayzPath;
        if (!string.IsNullOrWhiteSpace(d.ToolsPath)) ToolsPathBox.Text = d.ToolsPath;
        if (!string.IsNullOrWhiteSpace(d.ServerPath)) ServerPathBox.Text = d.ServerPath;
        RefreshPathStatuses();
        UpdateNextEnabled();
    }

    private void OnInstallTools(object sender, RoutedEventArgs e)
    {
        bool ok = SteamInstall.Install(SteamInstall.DayZTools);
        ToolsInstallHint.Visibility = Visibility.Visible;
        ToolsInstallHint.Text = ok
            ? "Steam is installing DayZ Tools — finish it there, then click Re-detect."
            : "Couldn't launch Steam — is it installed?";
    }

    private void OnInstallServer(object sender, RoutedEventArgs e)
    {
        bool ok = SteamInstall.Install(SteamInstall.DayZServer);
        ServerInstallHint.Visibility = Visibility.Visible;
        ServerInstallHint.Text = ok
            ? "Steam is installing DayZ Server — finish it there, then click Re-detect."
            : "Couldn't launch Steam — is it installed?";
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

    /// <summary>Prefill for the work folder: settings.ini WorkDirPath, else ~\Documents\DayZ Projects.</summary>
    private string DefaultWorkFolder()
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        var fromIni = tools.Length > 0 ? EnvDetect.WorkDir(tools) : null;
        return !string.IsNullOrWhiteSpace(fromIni)
            ? fromIni
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DayZ Projects");
    }

    private void OnMountWorkDrive(object sender, RoutedEventArgs e)
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        if (tools.Length == 0)
        {
            WorkDriveNote.Text = "DayZ Tools path is required to mount the work drive.";
            return;
        }
        var workFolder = WorkFolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(workFolder)) workFolder = DefaultWorkFolder();
        try { Directory.CreateDirectory(workFolder); } catch { /* surfaced via mount failure */ }

        var exe = Path.Combine(tools, "Bin", "WorkDrive", "WorkDrive.exe");
        WorkDrive.Mount(exe, workFolder);
        RefreshWorkDrive();
    }

    private void OnVerifyTools(object sender, RoutedEventArgs e)
    {
        var ok = SteamInstall.Validate(SteamInstall.DayZTools);
        VerifyHint.Visibility = Visibility.Visible;
        VerifyHint.Text = ok
            ? "Steam is verifying DayZ Tools — let it finish, then click Mount again."
            : "Couldn't launch Steam — is it installed and running?";
    }

    // ---- Step 4: Game data ----------------------------------------------

    private async void OnExtractGameData(object sender, RoutedEventArgs e)
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        var dayz = DayzPathBox.Text?.Trim() ?? "";
        if (tools.Length == 0)
        {
            GameDataNote.Text = "Set the DayZ Tools path on the Paths step first.";
            return;
        }
        if (!WorkDrive.IsMounted())
        {
            GameDataNote.Text = "P: is not mounted — mount P: on the previous (Work drive) step first.";
            return;
        }
        var exe = Path.Combine(tools, "Bin", "WorkDrive", "WorkDrive.exe");
        if (!File.Exists(exe))
        {
            GameDataNote.Text = @"WorkDrive.exe not found under <Tools>\Bin\WorkDrive. Use 'Open DayZ Tools' instead.";
            return;
        }

        ExtractBtn.IsEnabled = false;
        GameDataNote.Text = "Extracting… (this can take several minutes)";
        try
        {
            var (ok, output) = await Task.Run(() => WorkDrive.ExtractGameData(exe, dayz, @"P:\"));
            GameDataNote.Text = ok
                ? "Game data extracted to P:."
                : "Extraction failed." + (string.IsNullOrWhiteSpace(output) ? "" : "\r\n" + output);
        }
        finally
        {
            ExtractBtn.IsEnabled = true;
        }
    }

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
