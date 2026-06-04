using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Mods;
using Dzl.Core.Env;
using Dzl.Core.Tools;
using Dzl.Core.Projects;
using Dzl.Core.Servers;
using Dzl.Tray;

namespace Dzl.Tray.ViewModels;

/// <summary>
/// Backs <c>MainWindow</c>: a mod checklist (reorderable, side-cycle), live status
/// line, argv preview, profile switcher and four live log panes. Edits persist to the
/// active profile; server/client ops go through the tray's own <see cref="LauncherService"/>
/// (the tray process is the IPC authority, so the window calls it directly rather than
/// routing back through a pipe).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly string _configPath;
    // NOT readonly: ResolveActive picks the file to save to (active preset's .json, or the base
    // config when no preset is active). Switching presets changes that target, so Reload() must
    // re-assign it — otherwise saves land in the previously-active file and the current preset's
    // edits silently vanish on the next reload.
    private string _savePath;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _statusTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly LauncherService _svc;
    private DzlConfig _cfg;
    private bool _suppressPersist;
    private bool _disposed;

    [ObservableProperty] private string _mode = "debug";
    [ObservableProperty] private string _statusLine = "loading…";

    // Structured status (drives the Fluent top-bar pills + dashboard summary).
    [ObservableProperty] private int _port;
    [ObservableProperty] private bool _serverUp;
    [ObservableProperty] private bool _clientUp;
    [ObservableProperty] private string _serverStatus = "down";
    [ObservableProperty] private string _clientStatus = "down";

    /// <summary>True when the named-pipe automation/IPC server is actually running this session
    /// (read once from <see cref="App.AutomationServerRunning"/> in the ctor). Drives the MCP
    /// status pill dot. Static at runtime — the server's started/not at launch and never toggles.</summary>
    public bool AutomationOn { get; }

    /// <summary>Human-readable automation state for the MCP pill ("on"/"off").</summary>
    public string AutomationStatus => AutomationOn ? "on" : "off";

    /// <summary>Drives the "MCP" nav-rail item's visibility: only shown once the automation
    /// server is actually running this session (so the setup guide is discoverable in context).</summary>
    public bool ShowMcpTab => AutomationOn;

    /// <summary>Full <c>claude mcp add</c> command that registers the dzl stdio MCP server in
    /// Claude Code, with <c>DZL_CONFIG</c> pinned to the same config path this app uses. Built
    /// once in the ctor; the resolved executable is best-effort (exe → dll+dotnet → placeholder).</summary>
    public string McpRegisterCommand { get; }

    /// <summary>The MCP tool names exposed by <c>Dzl.Mcp.DzlMcpTools</c> (shown on the MCP page).</summary>
    public IReadOnlyList<string> McpTools { get; } = new[]
    {
        "status", "list_mods", "list_presets", "set_preset", "logs",
        "start", "stop", "restart", "list_tools", "open_tool",
        "convert_paa", "pack_pbo", "unbinarize", "work_drive_action",
    };

    /// <summary>Resolve how to invoke the Dzl.Mcp stdio server, best-effort: a sibling
    /// <c>Dzl.Mcp.exe</c> (run directly), else a sibling <c>Dzl.Mcp.dll</c> (via <c>dotnet</c>),
    /// else a clearly-marked placeholder dll path (dev/unbuilt — must be built or published).</summary>
    private static string ResolveMcpCommand()
    {
        var baseDir = AppContext.BaseDirectory;
        var exe = Path.Combine(baseDir, "Dzl.Mcp.exe");
        if (File.Exists(exe)) return Quote(exe);
        var dll = Path.Combine(baseDir, "Dzl.Mcp.dll");
        if (File.Exists(dll)) return $"dotnet {Quote(dll)}";
        // Dev / unbuilt: point at where the dll would live; it must be built or published first.
        return $"dotnet {Quote(dll)}";
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    /// <summary>True when <see cref="Mode"/> is "normal" (drives the mode ToggleSwitch).</summary>
    public bool IsNormalMode => Mode == "normal";

    partial void OnModeChanged(string value) => OnPropertyChanged(nameof(IsNormalMode));
    [ObservableProperty] private string _serverArgv = "";
    [ObservableProperty] private string _clientArgv = "";
    [ObservableProperty] private string _activePreset = "";
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _selectedPreset = "";
    [ObservableProperty] private string _modFilter = "";
    [ObservableProperty] private ModRowVm? _selectedMod;

    // --- Logs page state --------------------------------------------------
    // The four live log panes (script/rpt/adm/client) modeled as a reorderable collection
    // so every view mode binds the same items. Order is presentation-only (not persisted).
    public ObservableCollection<LogPaneVm> LogPanes { get; } = new();

    /// <summary>Active Logs layout: "grid" | "list" | "tabs" | "focus".</summary>
    [ObservableProperty] private string _logsViewMode = "grid";

    /// <summary>The pane shown in Focus mode (and a quick-jump in other modes).</summary>
    [ObservableProperty] private LogPaneVm? _selectedLogPane;

    /// <summary>gong-wpf-dragdrop drop handler for reordering <see cref="LogPanes"/>.</summary>
    public LogsDropHandler LogsDropHandler { get; }

    public ObservableCollection<ModRowVm> Mods { get; } = new();
    public ObservableCollection<string> Presets { get; } = new();

    /// <summary>Enabled mods that run on the SERVER (side in {both, server}), formatted as the
    /// mod Name with a "(side)" suffix when the side isn't "both" (e.g. "@Admin (server)").
    /// Drives the Dashboard left column's active-mods card. Refreshed by <see cref="RefreshActiveMods"/>.</summary>
    public ObservableCollection<string> ServerMods { get; } = new();

    /// <summary>Enabled mods that run on the CLIENT (side in {both, client}), formatted as the
    /// mod Name with a "(side)" suffix when the side isn't "both" (e.g. "@UI (client)").
    /// Drives the Dashboard right column's active-mods card. Refreshed by <see cref="RefreshActiveMods"/>.</summary>
    public ObservableCollection<string> ClientMods { get; } = new();

    /// <summary>Count of <see cref="ServerMods"/> (for the "Active mods (N)" header).</summary>
    public int ServerModsCount => ServerMods.Count;

    /// <summary>Count of <see cref="ClientMods"/> (for the "Active mods (N)" header).</summary>
    public int ClientModsCount => ClientMods.Count;

    /// <summary>Allowed values for the inline Side combo column (both|server|client).</summary>
    public static IReadOnlyList<string> Sides { get; } = new[] { "both", "server", "client" };

    /// <summary>Summary for the Mods toolbar count label ("N mods, M enabled").</summary>
    public string ModsSummary => $"{Mods.Count} mods, {Mods.Count(m => m.Enabled)} enabled";

    /// <summary>Snapshot of the current config (for the Settings/Params dialogs).</summary>
    public DzlConfig Cfg => _cfg;

    /// <summary>The active profile save path (where edits persist).</summary>
    public string SavePath => _savePath;

    /// <summary>The config path the window/VM resolved (for opening folders, presets).</summary>
    public string ConfigFilePath => _configPath;

    /// <summary>Filtered view over <see cref="Mods"/> bound by the ListBox; reorder
    /// commands still mutate the underlying collection.</summary>
    public ICollectionView ModsView { get; }

    /// <summary>gong-wpf-dragdrop drop handler for the Mods DataGrid (maps view drops back
    /// onto the underlying <see cref="Mods"/> collection). Bound by the grid in XAML.</summary>
    public ModsDropHandler ModsDropHandler { get; }

    public MainViewModel(string configPath)
    {
        _configPath = configPath;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _svc = new LauncherService(configPath);
        AutomationOn = App.AutomationServerRunning;
        McpRegisterCommand =
            $"claude mcp add dzl --env DZL_CONFIG=\"{configPath}\" -- {ResolveMcpCommand()}";

        Profiles.EnsureDefault(configPath);
        var (cfg, savePath, active) = Profiles.ResolveActive(configPath);
        _cfg = cfg;
        _savePath = savePath;
        ActivePreset = active;
        SelectedPreset = active;
        Mode = string.IsNullOrEmpty(cfg.Mode) ? "debug" : cfg.Mode;

        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = FilterMod;
        ModsDropHandler = new ModsDropHandler(this);
        LogsDropHandler = new LogsDropHandler(this);

        LogPanes.Add(new LogPaneVm("script", "Script"));
        LogPanes.Add(new LogPaneVm("rpt", "RPT"));
        LogPanes.Add(new LogPaneVm("adm", "ADM"));
        LogPanes.Add(new LogPaneVm("client", "Client"));
        SelectedLogPane = LogPanes[0];

        LoadMods();
        LoadPresets();
        RefreshPreview();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();

        StartLogTails();
    }

    private void LoadMods()
    {
        foreach (var row in Mods) row.Changed -= OnModChanged;
        Mods.Clear();
        var merged = ModDiscovery.Merge(_cfg.Mods, ModDiscovery.Discover(_cfg.ScanRoots));
        foreach (var m in merged)
        {
            var row = new ModRowVm
            {
                Enabled = m.Enabled,
                Name = m.Name,
                Path = m.Path,
                Side = m.Side,
                Missing = m.Missing,
            };
            row.Changed += OnModChanged;
            Mods.Add(row);
        }
        RenumberOrder();
        OnPropertyChanged(nameof(ModsSummary));
    }

    /// <summary>Rewrites each row's 1-based <see cref="ModRowVm.Order"/> from its position
    /// in the underlying <see cref="Mods"/> collection. Called after load and every reorder
    /// so the grid's "#" column matches the real load order.</summary>
    private void RenumberOrder()
    {
        for (int i = 0; i < Mods.Count; i++) Mods[i].Order = i + 1;
    }

    /// <summary>Move a mod within the underlying collection (used by drag-reorder), then
    /// renumber + persist. Indices are into <see cref="Mods"/>, not the filtered view.</summary>
    public void MoveMod(int from, int to)
    {
        if (from < 0 || from >= Mods.Count) return;
        if (to < 0 || to >= Mods.Count || to == from) return;
        Mods.Move(from, to);
        Persist();
    }

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var p in Profiles.List(_configPath)) Presets.Add(p);
        SelectedPreset = ActivePreset;
    }

    private void OnModChanged()
    {
        if (_suppressPersist) return;
        Persist();
    }

    // --- Persist / preview -------------------------------------------------

    private void Persist()
    {
        var entries = Mods.Select(m => new ModEntry
        {
            Path = m.Path,
            Enabled = m.Enabled,
            Side = m.Side,
        }).ToList();
        _cfg = _cfg with { Mods = entries, Mode = Mode };
        ConfigStore.Save(_cfg, _savePath);
        // Reorder commands mutate the ObservableCollection via Move(); renumber the
        // "#" column and refresh the filtered view so its ordering stays in sync.
        RenumberOrder();
        ModsView.Refresh();
        OnPropertyChanged(nameof(ModsSummary));
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        ServerArgv = ProcessManager.ServerExe(_cfg, Mode) + " " + string.Join(" ", ArgvBuilder.Build(Mode, "server", _cfg));
        ClientArgv = ProcessManager.ClientExe(_cfg, Mode) + " " + string.Join(" ", ArgvBuilder.Build(Mode, "client", _cfg));
        RefreshActiveMods();
    }

    /// <summary>Rebuild the per-target active-mod lists from the live <see cref="Mods"/> rows
    /// (enabled + side). Server runs side in {both, server}; client runs side in {both, client}.
    /// A "(side)" suffix is appended when the side isn't "both". Clears+repopulates on the UI thread.</summary>
    private void RefreshActiveMods()
    {
        void Rebuild()
        {
            ServerMods.Clear();
            ClientMods.Clear();
            foreach (var m in Mods)
            {
                if (!m.Enabled) continue;
                var label = m.Side == "both" ? m.Name : $"{m.Name} ({m.Side})";
                if (m.Side is "both" or "server") ServerMods.Add(label);
                if (m.Side is "both" or "client") ClientMods.Add(label);
            }
            OnPropertyChanged(nameof(ServerModsCount));
            OnPropertyChanged(nameof(ClientModsCount));
        }

        if (_dispatcher.CheckAccess()) Rebuild();
        else _dispatcher.BeginInvoke(Rebuild);
    }

    // --- Mod filter --------------------------------------------------------

    private bool FilterMod(object obj)
    {
        if (string.IsNullOrEmpty(ModFilter)) return true;
        if (obj is not ModRowVm m) return true;
        return (m.Name?.Contains(ModFilter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (m.Path?.Contains(ModFilter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnModFilterChanged(string value) => ModsView.Refresh();

    private void RefreshStatus()
    {
        try
        {
            var r = new LauncherService(_configPath).Status();
            var srv = r.Server.State == "up"
                ? $"up ({r.Server.Source}/{r.Server.Mode} pid {r.Server.Pid})"
                : "down";
            var cli = r.Client.State == "up"
                ? $"up ({r.Client.Source}/{r.Client.Mode} pid {r.Client.Pid})"
                : "down";
            Port = r.Port;
            ServerUp = r.Server.State == "up";
            ClientUp = r.Client.State == "up";
            ServerStatus = srv;
            ClientStatus = cli;
            StatusLine = $"mode {r.Mode} · port {r.Port} · preset {(string.IsNullOrEmpty(r.ActivePreset) ? "—" : r.ActivePreset)} · server {srv} · client {cli}";
        }
        catch (Exception ex)
        {
            StatusLine = "status error: " + ex.Message;
        }
    }

    // --- Server / client ops (background; call the tray's LauncherService) -

    private void RunOp(Action op) => Task.Run(() =>
    {
        try { op(); } catch { /* surfaced via status poll */ }
        finally { _dispatcher.BeginInvoke(RefreshStatus); }
    });

    [RelayCommand]
    private void StartServer() => RunOp(() => _svc.StartTarget("server", Mode));

    [RelayCommand]
    private void StopServer() => RunOp(() => _svc.StopTarget("server"));

    [RelayCommand]
    private void RestartServer() => RunOp(() => _svc.RestartTarget("server", Mode));

    [RelayCommand]
    private void StartClient() => RunOp(() => _svc.StartTarget("client", Mode));

    [RelayCommand]
    private void StopClient() => RunOp(() => _svc.StopTarget("client"));

    [RelayCommand]
    private void RestartClient() => RunOp(() => _svc.RestartTarget("client", Mode));

    /// <summary>Copy the <c>claude mcp add</c> registration command to the clipboard
    /// (clipboard access can throw if another process holds it; swallow best-effort).</summary>
    [RelayCommand]
    private void CopyMcpCommand()
    {
        try { System.Windows.Clipboard.SetText(McpRegisterCommand); }
        catch { /* clipboard busy/unavailable — best-effort */ }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = Mode == "debug" ? "normal" : "debug";
        Persist();
    }

    // --- Mod ordering / side ----------------------------------------------

    private int SelIndex => SelectedMod is null ? -1 : Mods.IndexOf(SelectedMod);

    [RelayCommand]
    private void MoveUp()
    {
        var i = SelIndex;
        if (i <= 0) return;
        Mods.Move(i, i - 1);
        Persist();
    }

    [RelayCommand]
    private void MoveDown()
    {
        var i = SelIndex;
        if (i < 0 || i >= Mods.Count - 1) return;
        Mods.Move(i, i + 1);
        Persist();
    }

    [RelayCommand]
    private void ToTop()
    {
        var i = SelIndex;
        if (i <= 0) return;
        Mods.Move(i, 0);
        Persist();
    }

    [RelayCommand]
    private void ToBottom()
    {
        var i = SelIndex;
        if (i < 0 || i >= Mods.Count - 1) return;
        Mods.Move(i, Mods.Count - 1);
        Persist();
    }

    [RelayCommand]
    private void CycleSide()
    {
        if (SelectedMod is null) return;
        SelectedMod.Side = SelectedMod.Side switch
        {
            "both" => "server",
            "server" => "client",
            _ => "both",
        };
        // ModRowVm.Changed -> OnModChanged -> Persist already fires.
    }

    [RelayCommand]
    private void Rescan()
    {
        _suppressPersist = true;
        try { LoadMods(); }
        finally { _suppressPersist = false; }
        RefreshPreview();
    }

    // --- Presets -----------------------------------------------------------
    //
    // All preset ops run DIRECTLY against Profiles/Core (quick file I/O). The tray
    // process is the IPC authority, so routing through ControlPlane/PipeClient here
    // would block on a synchronous named-pipe round-trip to our OWN PipeServer and
    // deadlock the UI thread. Never reintroduce a ControlPlane call in these paths.

    /// <summary>Set true while <see cref="Reload"/> repopulates <see cref="Presets"/> so the
    /// ComboBox's SelectionChanged doesn't fire a re-switch during programmatic refresh.</summary>
    public bool SuppressPresetSwitch { get; private set; }

    [RelayCommand]
    private void SwitchPreset() => SwitchToPreset(SelectedPreset);

    [RelayCommand]
    private void SavePreset()
    {
        var name = string.IsNullOrWhiteSpace(NewPresetName) ? ActivePreset : NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        // Make sure the live UI state (mods/order/mode) is in _cfg before snapshotting.
        Persist();
        Profiles.Save(_cfg, name, _configPath);
        Profiles.SetActive(name, _configPath);
        NewPresetName = "";
        Reload();
    }

    /// <summary>Switch to a named preset (used by the top-bar combo and menu items).</summary>
    public void SwitchToPreset(string? name)
    {
        if (SuppressPresetSwitch) return;
        if (string.IsNullOrEmpty(name) || name == ActivePreset) return;
        Profiles.SetActive(name, _configPath);
        Reload();
    }

    /// <summary>Delete a preset (the top-bar combo's current selection if none given), with
    /// confirmation. If it was active, clears the active marker so the default reseeds.</summary>
    [RelayCommand]
    private void DeletePreset(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? SelectedPreset : name.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var ok = System.Windows.MessageBox.Show(
            $"Delete preset \"{name}\"? This cannot be undone.",
            "Delete preset", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        Profiles.Delete(name, _configPath);
        if (ActivePreset == name) Profiles.SetActive("", _configPath);
        Reload();
    }

    // --- Params / Config dialogs (called from MainWindow menu handlers) ----

    /// <summary>Current per-target/per-mode params list for the given target.</summary>
    public List<string> CurrentParams(string target, string mode) =>
        (target, mode) switch
        {
            ("server", "normal") => _cfg.ServerParamsNormal,
            ("server", _) => _cfg.ServerParamsDebug,
            ("client", "normal") => _cfg.ClientParamsNormal,
            _ => _cfg.ClientParamsDebug,
        };

    /// <summary>Defaults for the given target/mode params list (for the Reset button).</summary>
    public static List<string> DefaultParams(string target, string mode)
    {
        var d = DzlConfig.Default();
        return (target, mode) switch
        {
            ("server", "normal") => d.ServerParamsNormal,
            ("server", _) => d.ServerParamsDebug,
            ("client", "normal") => d.ClientParamsNormal,
            _ => d.ClientParamsDebug,
        };
    }

    /// <summary>Apply an edited params list to the matching cfg slot, save, refresh preview.</summary>
    public void ApplyParams(string target, string mode, List<string> values)
    {
        _cfg = (target, mode) switch
        {
            ("server", "normal") => _cfg with { ServerParamsNormal = values },
            ("server", _) => _cfg with { ServerParamsDebug = values },
            ("client", "normal") => _cfg with { ClientParamsNormal = values },
            _ => _cfg with { ClientParamsDebug = values },
        };
        ConfigStore.Save(_cfg, _savePath);
        RefreshPreview();
    }

    /// <summary>Apply an edited config (from the Settings dialog): save then full reload.</summary>
    public void ApplyConfig(DzlConfig edited)
    {
        ConfigStore.Save(edited, _savePath);
        Reload();
    }

    public void Reload()
    {
        var (cfg, savePath, active) = Profiles.ResolveActive(_configPath);
        _cfg = cfg;
        _savePath = savePath;   // active preset changed -> save target must follow it
        ActivePreset = active;
        Mode = string.IsNullOrEmpty(cfg.Mode) ? "debug" : cfg.Mode;
        _suppressPersist = true;
        SuppressPresetSwitch = true;
        try
        {
            LoadMods();
            LoadPresets();
        }
        finally
        {
            _suppressPersist = false;
            SuppressPresetSwitch = false;
        }
        RefreshPreview();
    }

    // --- Live log tailing --------------------------------------------------

    private void StartLogTails()
    {
        var paths = LogResolver.Resolve(_cfg.ProfilesPath, _cfg.ClientProfilesPath);
        foreach (var pane in LogPanes)
        {
            var path = paths.GetValueOrDefault(pane.Key);
            pane.Path = path;
            pane.FileName = string.IsNullOrEmpty(path) ? "(none)" : Path.GetFileName(path);
            Tail(pane, path);
        }
    }

    private void Tail(LogPaneVm pane, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        // Seed with the existing tail so the pane isn't empty.
        foreach (var line in LogTail.LastLines(path, 200)) pane.Append(line);
        _ = LogTail.Follow(path, line => _dispatcher.BeginInvoke(() => pane.Append(line)), _cts.Token);
    }

    /// <summary>Switch the Logs page layout (grid/list/tabs/focus).</summary>
    [RelayCommand]
    private void SetLogsView(string mode) => LogsViewMode = mode;

    /// <summary>Open the folder containing a pane's resolved log file in Explorer.</summary>
    [RelayCommand]
    private void OpenLogFolder(LogPaneVm? pane)
    {
        var path = pane?.Path;
        if (string.IsNullOrEmpty(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { /* best-effort; ignore */ }
    }

    /// <summary>Clear a pane's in-memory view (the underlying file is untouched).</summary>
    [RelayCommand]
    private void ClearLog(LogPaneVm? pane) => pane?.Clear();

    // --- Tools page --------------------------------------------------------

    /// <summary>Discovered DayZ Tools entries (cards on the Tools page).</summary>
    public ObservableCollection<ToolEntry> Tools { get; } = new();

    [ObservableProperty] private bool _workDriveMounted;

    /// <summary>Human-readable work-drive status for the Tools card.</summary>
    public string WorkDriveStatus => WorkDriveMounted ? "P: mounted ✓" : "P: not mounted ✗";

    partial void OnWorkDriveMountedChanged(bool value) => OnPropertyChanged(nameof(WorkDriveStatus));

    /// <summary>The configured DayZ Tools path (for the WorkDrive.exe lookup).</summary>
    public string ToolsPath => _cfg.DayzToolsPath;

    /// <summary>Re-enumerate the tools catalog and refresh the work-drive state. Called on
    /// Tools page show and via the Refresh button.</summary>
    public void RefreshTools()
    {
        Tools.Clear();
        foreach (var t in ToolCatalog.Discover(_cfg.DayzToolsPath)) Tools.Add(t);
        RefreshWorkDrive();
    }

    public void RefreshWorkDrive() => WorkDriveMounted = WorkDrive.IsMounted();

    /// <summary>Launch a tool GUI on a background task (no UI block). Missing exes return false.</summary>
    public void LaunchTool(ToolEntry tool) => Task.Run(() => { try { ToolLauncher.Launch(tool); } catch { } });

    public void MountWorkDrive()
    {
        var exe = Path.Combine(_cfg.DayzToolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
        var source = EnvDetect.WorkDir(_cfg.DayzToolsPath);
        Task.Run(() =>
        {
            try { WorkDrive.Mount(exe, source); } catch { }
            finally { _dispatcher.BeginInvoke(RefreshWorkDrive); }
        });
    }

    public void UnmountWorkDrive()
    {
        var exe = Path.Combine(_cfg.DayzToolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
        Task.Run(() =>
        {
            try { WorkDrive.Unmount(exe); } catch { }
            finally { _dispatcher.BeginInvoke(RefreshWorkDrive); }
        });
    }

    /// <summary>Resolve a CLI-wrappable tool exe path by key, or null if not present.</summary>
    public string? ToolExe(string key) => ToolCatalog.Find(_cfg.DayzToolsPath, key) is { Exists: true } t ? t.ExePath : null;

    /// <summary>Pack a PBO via Addon Builder on a background task; reports the combined output.</summary>
    public Task<PackResult> PackAsync(string addonExe, string src, string dst, string? prefix, string? sign) =>
        Task.Run(() => AddonBuilder.Pack(addonExe, src, dst, clear: true, packOnly: true,
            prefix: string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim(),
            signKey: string.IsNullOrWhiteSpace(sign) ? null : sign.Trim()));

    /// <summary>Plan a batch PAA conversion (suffix warnings) without running the exe.</summary>
    public List<PaaJob> PlanPaa(string dir, bool recursive) => ImageToPaa.PlanFolder(dir, recursive);

    /// <summary>Run a batch PAA conversion on a background task, streaming per-file results.</summary>
    public Task<List<PaaResult>> ConvertPaaAsync(string paaExe, string dir, bool recursive, IProgress<PaaResult>? progress) =>
        Task.Run(() => ImageToPaa.ConvertFolder(paaExe, dir, recursive, progress));

    /// <summary>Unbinarize a .bin via CfgConvert on a background task.</summary>
    public Task<(bool ok, string output)> UnbinarizeAsync(string cfgExe, string binPath, string outCpp) =>
        Task.Run(() => CfgConvert.Unbinarize(cfgExe, binPath, outCpp));

    // === My Mods (source projects) page ===================================

    /// <summary>Mod source projects discovered under the ProjectsRoot (drives the My Mods page).</summary>
    public ObservableCollection<ModProject> ModProjects { get; } = new();

    /// <summary>Resolved ProjectsRoot (configured value or the %USERPROFILE% fallback). Shown on
    /// the My Mods / Servers pages so the user knows where dzl creates things.</summary>
    public string ProjectsRoot => ProjectPaths.Root(_cfg);

    /// <summary>The config directory (author cache lives here, next to config.json).</summary>
    private string ConfigDir => Path.GetDirectoryName(_configPath) ?? ".";

    /// <summary>Cached author handle (for prefilling the New mod form), or "".</summary>
    public string CachedAuthor => ModScaffold.CachedAuthor(ConfigDir) ?? "";

    /// <summary>Re-enumerate mod source projects. Called on My Mods page show + after create/import/link.</summary>
    public void RefreshModProjects()
    {
        ModProjects.Clear();
        foreach (var p in Dzl.Core.Projects.ModProjects.Discover(ProjectsRoot)) ModProjects.Add(p);
        OnPropertyChanged(nameof(ProjectsRoot));
    }

    /// <summary>Scaffold a new mod project + link P:\&lt;Mod&gt;. Caches the author. Returns a status line.</summary>
    public string CreateModProject(string name, string author)
    {
        var root = ProjectsRoot;
        var res = ModScaffold.Scaffold(root, name, author);
        if (!res.Ok) return $"✗ {res.Message}";
        if (!string.IsNullOrWhiteSpace(author)) ModScaffold.SaveAuthor(ConfigDir, author);
        var link = Junction.Ensure(ProjectPaths.WorkDriveLink(name), ProjectPaths.ModDir(root, name));
        RefreshModProjects();
        return link.Ok
            ? $"✓ created {name} + linked P:\\{name}"
            : $"✓ created {name}  (⚠ P:\\ link: {link.Detail})";
    }

    /// <summary>Import an external mod source folder as a project (non-invasive link). Returns a status line.</summary>
    public string ImportModProject(string source, string? name)
    {
        var res = ModImport.Import(ProjectsRoot, source, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
        RefreshModProjects();
        return res.Ok ? $"✓ imported → {res.ModDir}" : $"✗ {res.Message}";
    }

    /// <summary>(Re)create the P:\&lt;Mod&gt; junction for a project. Returns a status line.</summary>
    public string QuickJunction(string name)
    {
        var link = Junction.Ensure(ProjectPaths.WorkDriveLink(name), ProjectPaths.ModDir(ProjectsRoot, name));
        RefreshModProjects();
        return link.Ok ? $"✓ {name}: {link.Detail}" : $"✗ {name}: {link.Detail}";
    }

    // === Servers (instances) page =========================================

    /// <summary>Server instances discovered under &lt;ProjectsRoot&gt;\servers (drives the Servers page).</summary>
    public ObservableCollection<ServerInstance> Servers { get; } = new();

    /// <summary>Map choices for the New server form (aliases the Core resolves to mission templates).</summary>
    public static IReadOnlyList<string> Maps { get; } = new[] { "chernarus", "livonia", "sakhal" };

    public void RefreshServers()
    {
        Servers.Clear();
        foreach (var s in new ServerService(_configPath).List()) Servers.Add(s);
        OnPropertyChanged(nameof(ProjectsRoot));
    }

    /// <summary>Create a new server instance (scaffold + atomic preset) and reload so it's active.
    /// Returns a status line.</summary>
    public string CreateServer(string name, string map, int? port)
    {
        var res = new ServerService(_configPath).Create(name, map, port);
        Reload();              // active preset changed → refresh mods/paths/preset list
        RefreshServers();
        return res.Ok ? $"✓ {res.Message}  (port {res.Port})" : $"✗ {res.Message}";
    }

    /// <summary>Switch the active preset to a server instance's preset (by name).</summary>
    public string UseServer(string name)
    {
        var res = SetPresetByName(name);
        return res ? $"✓ active server → {name}" : $"✗ no preset '{name}'";
    }

    /// <summary>Activate a preset by name + reload; false if it doesn't exist.</summary>
    private bool SetPresetByName(string name)
    {
        if (!Profiles.List(_configPath).Contains(name)) return false;
        Profiles.SetActive(name, _configPath);
        Reload();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusTimer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
