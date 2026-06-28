using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Mods;
using Dzl.Core.Projects;
using Dzl.Core.Tools;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

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
    // 0-based step indices (array order). A "Project folder" step sits between Paths and Work drive.
    private const int CheckStep = 1;
    private const int PathsStep = 2;
    private const int ProjStep = 3;
    private const int WorkDriveStep = 4;
    private const int GameDataStep = 5;
    private const int ServerStep = 6;
    private const int ModsStep = 7;
    private const int FinishStep = 8;
    private const int LastStep = FinishStep;
    private readonly string _configPath;
    private int _step;

    private readonly Border[] _stepRows;
    private readonly UIElement[] _pages;

    public SetupWizardWindow(string configPath)
    {
        InitializeComponent();
        _configPath = configPath;

        // Order matters: the array INDEX is the authoritative step number (the Page*/StepRow* x:Name
        // suffixes are historical and no longer match the displayed numbers). Check sits at index 1.
        _stepRows = new[] { StepRow0, StepRowCheck, StepRow1, StepRowProj, StepRow2, StepRow3, StepRow4, StepRow6, StepRow7 };
        _pages = new UIElement[] { Page0, PageCheck, Page1, PageProj, Page2, Page3, Page4, Page6, Page7 };

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

    /// <summary>Jump to a step — used by the headless wizard smoke; normal flow uses Back/Next.</summary>
    public void GoToStep(int step) => ShowStep(step);

    /// <summary>Per-step entry logic (detection, prefill, status refresh).</summary>
    private void OnEnterStep(int step)
    {
        switch (step)
        {
            case CheckStep: // Environment check — run diagnostics
                RunEnvCheck();
                break;

            case PathsStep: // detect + prefill (only if still blank, so re-entry keeps edits)
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
                SteamCmdBox.Text = SteamCmd.DownloadServerScript(ServerDirForSteamCmd());  // advanced expander
                break;

            case ProjStep: // Main project folder — prefill (config root, else default) + validate
                if (string.IsNullOrWhiteSpace(ProjectsRootBox.Text))
                    ProjectsRootBox.Text = ProjectPaths.Root(CurrentWizardConfig());
                RefreshProjectsRootStatus();
                break;

            case WorkDriveStep: // prefill the work folder (becomes P:)
                if (string.IsNullOrWhiteSpace(WorkFolderBox.Text))
                    WorkFolderBox.Text = DefaultWorkFolder();
                RefreshWorkDrive();
                break;

            case GameDataStep:
                GameDataNote.Text = string.IsNullOrWhiteSpace(ToolsPathBox.Text)
                    ? "DayZ Tools path not set (Paths step) — set it to open the tools from here."
                    : "Vanilla data extracts to P:\\ (mount it on the Work drive step first).";
                break;

            case ServerStep: // Server instance — show the target dir under the project folder
                UpdateServerTarget();
                break;

            case ModsStep: // prefill scan roots
                if (string.IsNullOrWhiteSpace(ScanRootsBox.Text))
                    ScanRootsBox.Text = string.Join("\r\n", DefaultScanRoots());
                break;

            case FinishStep: // summary
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
        if (SteamCmdBox is not null)   // keep the advanced SteamCMD command in sync with the server dir
            SteamCmdBox.Text = SteamCmd.DownloadServerScript(ServerDirForSteamCmd());
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
        bool registered = EnvDetect.ToolsRegistered();
        if (registered)
        {
            ToolsRegStatus.Text = "✓ DayZ Tools initialized";
            ToolsRegStatus.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
        }
        else
        {
            ToolsRegStatus.Text =
                "⚠ DayZ Tools not initialized — open it once via Steam (writes the registry it needs), then Re-check.";
            ToolsRegStatus.Foreground = System.Windows.Media.Brushes.Goldenrod;
        }

        // Does the chosen work folder exist on disk yet?
        var workFolder = WorkFolderBox.Text?.Trim() ?? "";
        bool folderExists = workFolder.Length > 0 && Directory.Exists(workFolder);
        if (WorkFolderStatus is not null)
        {
            WorkFolderStatus.Text = folderExists ? "✓ folder exists" : "• will be created on Mount";
            WorkFolderStatus.Foreground = folderExists
                ? System.Windows.Media.Brushes.MediumSeaGreen
                : (System.Windows.Media.Brush?)TryFindResource("TextFillColorTertiaryBrush")
                    ?? System.Windows.Media.Brushes.Gray;
        }

        bool mounted = WorkDrive.IsMounted();
        var target = mounted ? WorkDrive.MountTarget("P:") : null;
        // mismatch = P: is mounted but points somewhere other than the chosen work folder
        bool mismatch = mounted && !string.IsNullOrWhiteSpace(target) && !WorkDrive.SamePath(target, workFolder);
        if (!mounted)
        {
            WorkDriveStatus.Text = "✗ P: is not mounted";
            WorkDriveStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        else if (string.IsNullOrWhiteSpace(target))
        {
            WorkDriveStatus.Text = "✓ P: is mounted (target unknown)";
            WorkDriveStatus.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
        }
        else if (!mismatch)
        {
            WorkDriveStatus.Text = $"✓ P: → {target} (matches work folder)";
            WorkDriveStatus.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
        }
        else
        {
            WorkDriveStatus.Text = $"⚠ P: → {target}  (a leftover/stale mount — NOT the folder above). Unmount it, or Re-mount P: to the folder above.";
            WorkDriveStatus.Foreground = System.Windows.Media.Brushes.Goldenrod;
        }

        // Mount enabled when Tools is initialized AND (P: not mounted OR mounted to the wrong folder
        // so we can re-point it). Re-label to make the re-point case obvious. Unmount only when mounted.
        // Mount is always available once Tools is initialized — mounting is idempotent and the
        // user may need to (re)mount even when a phantom/cross-session P: mapping is detected.
        MountBtn.IsEnabled = registered;
        MountBtn.Content = mounted ? "Re-mount P: here" : "Mount P: drive";
        UnmountBtn.IsEnabled = mounted;
        WdOpenToolsBtn.Appearance = registered
            ? Wpf.Ui.Controls.ControlAppearance.Secondary
            : Wpf.Ui.Controls.ControlAppearance.Primary;

        // The #1 cause of "shows mounted but no P:" — dzl running as admin sees admin-session
        // drive maps that Explorer + the game (normal user) don't, and vice versa.
        bool elevated = EnvDetect.IsElevated();
        bool linked = EnvDetect.LinkedConnectionsEnabled();
        if (elevated && !linked)
        {
            WorkDriveStatus.Text = "⚠ dzl is running as ADMINISTRATOR — admin-session drives differ from "
                + "Explorer/the game. A P: mounted here won't be seen by the (non-admin) game. Either run dzl "
                + "normally (not as admin), OR click 'Fix drive visibility' to share drive maps across both "
                + "(one-time, needs a reboot).";
            WorkDriveStatus.Foreground = System.Windows.Media.Brushes.Goldenrod;
        }
        // Offer the system fix only when it's relevant (elevated + not yet enabled).
        LinkedConnBtn.Visibility = (elevated && !linked) ? Visibility.Visible : Visibility.Collapsed;

        WorkDriveNote.Text = string.IsNullOrWhiteSpace(ToolsPathBox.Text)
            ? "Set the DayZ Tools path on the Paths step to enable mounting."
            : @"Mounts via <Tools>\Bin\WorkDrive\WorkDrive.exe.";
    }

    private void OnReCheckWorkDrive(object sender, RoutedEventArgs e) => RefreshWorkDrive();

    private void OnEnableLinkedConnections(object sender, RoutedEventArgs e)
    {
        var ok = EnvDetect.TryEnableLinkedConnections();
        System.Windows.MessageBox.Show(
            ok ? "Shared drive mappings enabled (EnableLinkedConnections=1).\n\nReboot for it to take effect — "
                 + "after that, a work drive mounted by either an admin or a normal app is visible to both."
               : "Couldn't write the setting — dzl needs to be running as administrator to change it.",
            "DayZ Labs — drive visibility",
            System.Windows.MessageBoxButton.OK,
            ok ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        RefreshWorkDrive();
    }

    private void OnWorkFolderChanged(object sender, TextChangedEventArgs e)
    {
        if (WorkDriveStatus is null) return; // pre-template
        RefreshWorkDrive();
    }

    /// <summary>Prefill for the work folder: settings.ini WorkDirPath, else ~\Documents\DayZ Projects.</summary>
    private string DefaultWorkFolder()
    {
        // If P: is already mounted, prefer the folder it ACTUALLY maps to — that's the live
        // truth and avoids a mismatch with settings.ini's nominal [ProjectDrive] path.
        if (WorkDrive.IsMounted())
        {
            var live = WorkDrive.MountTarget("P:");
            if (!string.IsNullOrWhiteSpace(live)) return live;
        }
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        var fromIni = tools.Length > 0 ? EnvDetect.WorkDir(tools) : null;
        return !string.IsNullOrWhiteSpace(fromIni)
            ? fromIni
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DayZ Projects");
    }

    private async void OnMountWorkDrive(object sender, RoutedEventArgs e)
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
        await RunWorkDriveOp("Mounting…", () =>
        {
            // If P: is already mounted to a DIFFERENT folder, unmount first so we can re-point it.
            if (WorkDrive.IsMounted() && !WorkDrive.SamePath(WorkDrive.MountTarget("P:"), workFolder))
                WorkDrive.Unmount(exe);
            WorkDrive.Mount(exe, workFolder);
        });
    }

    private async void OnUnmountWorkDrive(object sender, RoutedEventArgs e)
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        var exe = Path.Combine(tools, "Bin", "WorkDrive", "WorkDrive.exe");
        await RunWorkDriveOp("Unmounting…", () => WorkDrive.Unmount(exe));
    }

    // Run a WorkDrive.exe operation OFF the UI thread (its WaitForExit can block) so the window
    // never freezes; disable the buttons + show a note while it runs, then refresh.
    private async Task RunWorkDriveOp(string busyNote, Action op)
    {
        MountBtn.IsEnabled = false;
        UnmountBtn.IsEnabled = false;
        WorkDriveNote.Text = busyNote;
        try { await Task.Run(op); }
        catch (Exception ex) { WorkDriveNote.Text = "Work drive op failed: " + ex.Message; }
        RefreshWorkDrive();   // re-evaluates state + re-enables buttons appropriately
    }

    private void OnVerifyTools(object sender, RoutedEventArgs e)
    {
        var ok = SteamInstall.Validate(SteamInstall.DayZTools);
        VerifyHint.Visibility = Visibility.Visible;
        VerifyHint.Text = ok
            ? "Steam is verifying DayZ Tools — let it finish, then click Mount again."
            : "Couldn't launch Steam — is it installed and running?";
        RefreshWorkDrive();
    }

    private void OnOpenToolsForWorkDrive(object sender, RoutedEventArgs e)
    {
        // Launch THROUGH Steam (steam://run) — Steam sets the correct working dir + applies the
        // install-script registry. Starting DayZToolsLauncher.exe directly crashes ("can't find
        // e:\settings.ini") because it resolves settings.ini relative to its cwd.
        var ok = SteamInstall.Run(SteamInstall.DayZTools);
        VerifyHint.Visibility = Visibility.Visible;
        VerifyHint.Text = ok
            ? "Opening DayZ Tools via Steam — let it finish first-run setup, close it, then click Re-check."
            : "Couldn't reach Steam — is it installed and running?";
        RefreshWorkDrive();
    }

    // ---- Step 4: Game data ----------------------------------------------

    // Extract button: a real extract you watch (WorkDrive's progress console is shown).
    private void OnExtractGameData(object sender, RoutedEventArgs e) => _ = RunExtract(showProgress: true);

    // Re-check IS the same BankRev unpack — it's idempotent (a manifest skips PBOs already extracted at
    // their current timestamp), so re-running it is the authoritative verify. Quiet: only the summary.
    private void OnReCheckGameData(object sender, RoutedEventArgs e) => _ = RunExtract(showProgress: false);

    private async Task RunExtract(bool showProgress)
    {
        var tools = ToolsPathBox.Text?.Trim() ?? "";
        var game = DayzPathBox.Text?.Trim() ?? "";
        if (tools.Length == 0)
        {
            GameDataNote.Text = "Set the DayZ Tools path on the Paths step first.";
            return;
        }
        if (!Directory.Exists(game))
        {
            GameDataNote.Text = "Set a valid DayZ install path on the Paths step first.";
            return;
        }
        if (!WorkDrive.IsMounted())
        {
            GameDataNote.Text = "P: is not mounted — mount P: on the previous (Work drive) step first.";
            return;
        }
        var bankrev = ToolCatalog.Find(tools, "bankrev");
        if (bankrev is null || !bankrev.Exists)
        {
            GameDataNote.Text = @"BankRev.exe not found under <Tools>\Bin\PboUtils. Check the DayZ Tools path.";
            return;
        }

        ExtractBtn.IsEnabled = GameDataReCheckBtn.IsEnabled = false;
        ExtractRing.Visibility = Visibility.Visible;
        GameDataNote.Text = showProgress
            ? "Extracting vanilla PBOs to P: with BankRev. First run unpacks everything (a few minutes); "
              + "re-runs skip what's already current."
            : "Verifying… re-checking each PBO (instant when already extracted).";
        try
        {
            // dzl unpacks every game PBO itself via BankRev (reliable + incremental) instead of WorkDrive.exe
            // /ExtractGameData, whose built-in extract is unreliable. Idempotent: a manifest skips current PBOs.
            var r = await Task.Run(() => GameUnpack.UnpackAll(bankrev.ExePath, game, @"P:\", force: false,
                onItem: it => { if (showProgress) Dispatcher.BeginInvoke(() =>
                    GameDataNote.Text = $"[{it.Index}/{it.Total}] {Path.GetFileName(it.Pbo)} — {it.Status}"); }));
            var present = GameDataPresent();
            GameDataNote.Text = r.Ok
                ? $"✓ {r.Message}. Vanilla game data is in P:\\ (P:\\DZ)."
                : $"✗ {r.Message}" + (present ? "" : " — and P:\\DZ isn't there (is P: mounted in this session?).");
        }
        finally
        {
            ExtractRing.Visibility = Visibility.Collapsed;
            ExtractBtn.IsEnabled = GameDataReCheckBtn.IsEnabled = true;
        }
    }

    private static bool GameDataPresent() => Directory.Exists(@"P:\DZ") || Directory.Exists(@"P:\dz");

    private void OnOpenDayzTools(object sender, RoutedEventArgs e)
    {
        // Open via Steam (correct cwd + registry); starting the exe directly crashes on settings.ini.
        bool ok = SteamInstall.Run(SteamInstall.DayZTools);
        GameDataNote.Text = ok
            ? "Opening DayZ Tools via Steam. Click 'Extract Game Data' there to unpack vanilla PBOs to P:."
            : "Couldn't reach Steam — is it installed and running?";
    }

    // ---- Step (Project folder) ------------------------------------------

    private void OnProjectsRootChanged(object sender, TextChangedEventArgs e)
    {
        if (ProjectsRootStatus is null) return; // pre-template
        RefreshProjectsRootStatus();
    }

    /// <summary>Validate the chosen ProjectsRoot: an existing dzl tree (show mod/server counts),
    /// an existing plain folder (subfolders will be made), or a new folder (created on Finish).</summary>
    private void RefreshProjectsRootStatus()
    {
        var root = ProjectsRootBox.Text?.Trim() ?? "";
        if (root.Length == 0) { ProjectsRootStatus.Text = ""; return; }

        if (!Directory.Exists(root))
        {
            ProjectsRootStatus.Text = "• new — created on Finish (mods\\, build\\, servers\\, keys\\, workshop\\).";
            ProjectsRootStatus.Foreground = System.Windows.Media.Brushes.Goldenrod;
            return;
        }

        bool hasTree = Directory.Exists(ProjectPaths.ModsDir(root))
                    || Directory.Exists(ProjectPaths.ServersDir(root))
                    || Directory.Exists(ProjectPaths.BuildRoot(root))
                    || Directory.Exists(ProjectPaths.KeysDir(root, null));
        if (hasTree)
        {
            int mods = CountDirs(ProjectPaths.ModsDir(root));
            int servers = CountDirs(ProjectPaths.ServersDir(root));
            ProjectsRootStatus.Text = $"✓ existing dzl project folder — {mods} mod(s), {servers} server(s).";
        }
        else
        {
            ProjectsRootStatus.Text = "✓ existing folder — the dzl subfolders will be created on Finish.";
        }
        ProjectsRootStatus.Foreground = System.Windows.Media.Brushes.MediumSeaGreen;
    }

    private static int CountDirs(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Count() : 0; }
        catch { return 0; }
    }

    // ---- Step (Server instance) -----------------------------------------

    private void OnInstanceNameChanged(object sender, TextChangedEventArgs e)
    {
        if (ServerTargetNote is null) return; // pre-template
        UpdateServerTarget();
    }

    private string SelectedMap() =>
        (MapBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "chernarus";

    /// <summary>The project root the wizard will use: the typed value, else config/default.</summary>
    private string WizardProjectsRoot()
    {
        var root = ProjectsRootBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(root) ? ProjectPaths.Root(CurrentWizardConfig()) : root!;
    }

    /// <summary>Show where the instance lands (under the project folder) and flag an invalid name.</summary>
    private void UpdateServerTarget()
    {
        var name = InstanceNameBox.Text?.Trim() ?? "";
        bool valid = ProjectPaths.IsValidName(name);
        ServerTargetNote.Text = valid
            ? $"Created at:  {ProjectPaths.ServerDir(WizardProjectsRoot(), name)}"
            : "Created under your project folder's servers\\ folder.";
        ServerNameStatus.Text = (valid || name.Length == 0)
            ? ""
            : "✗ invalid name — letters, digits, underscores; must start with a letter.";
        ServerNameStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
    }

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
        var root = WizardProjectsRoot();
        var name = InstanceNameBox.Text?.Trim() ?? "";
        var roots = ScanRootLines();
        var server = ProjectPaths.IsValidName(name)
            ? $"{name} ({SelectedMap()})  →  {ProjectPaths.ServerDir(root, name)}"
            : "(none)";
        return
            $"DayZ install   : {Show(DayzPathBox.Text)}\r\n" +
            $"DayZ Tools     : {Show(ToolsPathBox.Text)}\r\n" +
            $"DayZ Server    : {Show(ServerPathBox.Text)}\r\n" +
            $"Project folder : {root}\r\n" +
            $"Server instance: {server}\r\n" +
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
        var root = ProjectsRootBox.Text?.Trim() ?? "";
        var roots = ScanRootLines();

        var cfg = defaults with
        {
            DayzPath = dayz,
            DayzToolsPath = tools,
            ProjectsRoot = root,   // "" → ProjectPaths.Root resolves to %USERPROFILE%\DayZProjects
            ScanRoots = roots.Count > 0 ? roots : defaults.ScanRoots,
        };

        // Persist the global config first so ServerService below reads the chosen ProjectsRoot.
        GlobalStore.Save(cfg.GlobalPart("default"), _configPath);

        // Create the standard project tree under the resolved root.
        var resolvedRoot = ProjectPaths.Root(cfg);
        foreach (var sub in new[] { "mods", "build", "servers", "keys", "workshop" })
        {
            try { Directory.CreateDirectory(Path.Combine(resolvedRoot, sub)); } catch { /* best-effort */ }
        }

        // Create the chosen server instance the modern way (scaffold + preset + activate). If the name
        // was blanked/invalid, just seed + activate a default preset so a usable config still exists.
        var name = InstanceNameBox.Text?.Trim() ?? "";
        if (ProjectPaths.IsValidName(name))
        {
            try { new ServerService(_configPath).Create(name, SelectedMap(), activate: true); }
            catch { /* best-effort; the user can create a server from the Servers tab later */ }
        }
        else
        {
            Profiles.EnsureDefault(_configPath);
            Profiles.SetActive("default", _configPath);
        }

        DialogResult = true;
        Close();
    }

    // ---- Shared folder picker (Tag = target TextBox x:Name) -------------

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        try
        {
            var dlg = new OpenFolderDialog();
            var current = (FindName(name) as TextBox)?.Text?.Trim();
            if (!string.IsNullOrEmpty(current))
            {
                // OpenFolderDialog throws on a mixed/forward-slash InitialDirectory — normalize.
                try { current = Path.GetFullPath(current); } catch { current = null; }
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                    dlg.InitialDirectory = current;
            }
            if (dlg.ShowDialog(this) == true && FindName(name) is TextBox tb)
                tb.Text = dlg.FolderName;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Couldn't open the folder picker:\n" + ex.Message, "dzl",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
