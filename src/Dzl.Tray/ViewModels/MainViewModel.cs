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
using Dzl.Core.Bases;
using Dzl.Core.Economy;
using Dzl.Core.Workshop;
using Dzl.Core.Vcs;
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
    // Separate token for the live log tails so they can be cancelled + restarted when the active
    // server changes (each server has its own profiles dirs → its own logs). Recreated by RetailLogs.
    private CancellationTokenSource _logCts = new();
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
    public ObservableCollection<ActiveModVm> ServerMods { get; } = new();

    /// <summary>Enabled mods that run on the CLIENT (side in {both, client}), formatted as the
    /// mod Name with a "(side)" suffix when the side isn't "both" (e.g. "@UI (client)").
    /// Drives the Dashboard right column's active-mods card. Refreshed by <see cref="RefreshActiveMods"/>.</summary>
    public ObservableCollection<ActiveModVm> ClientMods { get; } = new();

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

    /// <summary>Active instance name with a safe fallback (server instances can't be named "").</summary>
    private string ActiveName => string.IsNullOrEmpty(ActivePreset) ? "default" : ActivePreset;

    /// <summary>The config path the window/VM resolved (for opening folders, presets).</summary>
    public string ConfigFilePath => _configPath;

    /// <summary>Filtered view over <see cref="Mods"/> bound by the ListBox; reorder
    /// commands still mutate the underlying collection.</summary>
    public ICollectionView ModsView { get; }

    /// <summary>gong-wpf-dragdrop drop handler for the Mods DataGrid (maps view drops back
    /// onto the underlying <see cref="Mods"/> collection). Bound by the grid in XAML.</summary>
    public ModsDropHandler ModsDropHandler { get; }

    /// <summary>Backs the Economy "Types" tab (types.xml editor): grid, filters, batch ops, undo/redo,
    /// lint, dictionary suggestions. Extracted sub-VM; XAML binds via <c>TypesEditor.*</c>.</summary>
    public TypesEditorVm TypesEditor { get; }

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
        TypesEditor = new TypesEditorVm(configPath, onDictionariesChanged: () => _dictionaries?.Reload());
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
        _statusTimer.Tick += (_, _) => { RefreshStatus(); RefreshLogPaths(); RefreshWorkDrive(); };
        _statusTimer.Start();
        RefreshStatus();

        // Seed the GitHub/Steam pills once at startup (gh shells out — keep it off the UI thread;
        // login/logout flows re-run these on their own).
        RefreshSteamAccount();
        _ = RefreshGitHubAuthAsync();

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
                Kind = Dzl.Core.Mods.ModClassify.Classify(m.Path, _cfg),
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
        Profiles.Save(_cfg, ActiveName, _configPath);   // mods + mode are per-server
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
                if (m.Side is "both" or "server") ServerMods.Add(new ActiveModVm(label, m.Kind));
                if (m.Side is "both" or "client") ClientMods.Add(new ActiveModVm(label, m.Kind));
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

    /// <summary>Drop a mod from the active server's run-list (used to prune stale/"missing" entries that a
    /// Rescan can't touch — Rescan only re-discovers files on disk, it doesn't edit the saved run-list).</summary>
    [RelayCommand]
    private void RemoveMod(ModRowVm? row)
    {
        if (row is null) return;
        row.Changed -= OnModChanged;
        Mods.Remove(row);
        Persist();   // rebuilds the run-list from the remaining rows + saves to the active instance
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
        Profiles.Save(_cfg, ActiveName, _configPath);   // launch params are per-server
        RefreshPreview();
    }

    /// <summary>Apply an edited config (from the Settings dialog): save then full reload.</summary>
    public void ApplyConfig(DzlConfig edited)
    {
        // Settings edits global fields; persist both slices so per-server values (still shown on the
        // Settings page for now) also survive. Globals → config.json, per-server → active instance.
        GlobalStore.Save(edited.GlobalPart(ActiveName), _configPath);
        Profiles.Save(edited, ActiveName, _configPath);
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
        OnPropertyChanged(nameof(ResolvedWorkDriveSource));   // cfg overrides may have changed
        OnPropertyChanged(nameof(ResolvedKeysDir));
        OnPropertyChanged(nameof(ResolvedSigningKey));
        OnPropertyChanged(nameof(ProjectsRoot));
        OnPropertyChanged(nameof(ModsBlocked));
        NotifyWorkshopGate();
        RetailLogs();   // logs follow the active server's profiles
    }

    // --- Live log tailing --------------------------------------------------
    // Logs are per-server: they come from the active instance's ProfilesPath/ClientProfilesPath.
    // Switching the active server re-points them via RetailLogs() (called from Reload()).

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

    /// <summary>
    /// Poll (from the status timer) for the newest log file changing — e.g. a server just started and
    /// wrote a fresh script/RPT/ADM, or the active server changed. When any pane's resolved newest
    /// file differs from what it's tailing, re-tail so the panes follow the live files. No-op otherwise.
    /// </summary>
    private void RefreshLogPaths()
    {
        if (_disposed) return;
        var paths = LogResolver.Resolve(_cfg.ProfilesPath, _cfg.ClientProfilesPath);
        foreach (var pane in LogPanes)
        {
            var p = paths.GetValueOrDefault(pane.Key);
            if (!string.Equals(p, pane.Path, StringComparison.OrdinalIgnoreCase)) { RetailLogs(); return; }
        }
    }

    /// <summary>Cancel the current log follows and restart them against the active server's profiles
    /// (clears each pane first so you don't see the previous server's lines).</summary>
    private void RetailLogs()
    {
        var old = _logCts;
        _logCts = new CancellationTokenSource();
        old.Cancel();
        // Don't Dispose() the old CTS here: background Follow tasks may still hold its token for a
        // moment (Task.Delay/registrations) and disposing under them races into ObjectDisposedException.
        // Cancellation stops the loops; the orphaned CTS is collected by GC.
        foreach (var pane in LogPanes) pane.Clear();
        StartLogTails();
    }

    private void Tail(LogPaneVm pane, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        // Seed with the existing tail so the pane isn't empty (one batch).
        pane.AppendBatch(LogTail.LastLines(path, 200));
        // Run the follow loop on a background thread (Task.Run) so file reads never touch the UI
        // thread. New lines arrive BATCHED per poll cycle → one dispatcher hop per cycle (not per
        // line), so a server's startup spew can't flood/freeze the UI.
        var token = _logCts.Token;
        _ = Task.Run(() => LogTail.Follow(path,
            batch => _dispatcher.BeginInvoke(() => pane.AppendBatch(batch)), token));
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
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        Dzl.Tray.ShellOpen.Folder(dir);
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
        var source = WorkDriveSource;
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
    public ObservableCollection<ModProjectVm> ModProjects { get; } = new();

    /// <summary>Resolved ProjectsRoot (configured value or the %USERPROFILE% fallback). Shown on
    /// the My Mods / Servers pages so the user knows where dzl creates things.</summary>
    public string ProjectsRoot => ProjectPaths.Root(_cfg);

    /// <summary>The config directory (author cache lives here, next to config.json).</summary>
    private string ConfigDir => Path.GetDirectoryName(_configPath) ?? ".";

    /// <summary>Cached author handle (for prefilling the New mod form), or "".</summary>
    public string CachedAuthor => ModScaffold.CachedAuthor(ConfigDir) ?? "";

    // --- Mods page requirement gating -------------------------------------
    // Blocking (no projects root → the page can't create/manage anything) shows a full overlay; the lighter
    // "P: not mounted" case stays a banner (Build/Link disabled). See ModuleSettingsWindow for the gear modal.

    /// <summary>True when the Mods page has no usable projects root configured — show the blocking overlay.</summary>
    public bool ModsBlocked => string.IsNullOrWhiteSpace(_cfg.ProjectsRoot);

    /// <summary>Why the Mods page is blocked (shown in the overlay).</summary>
    public string ModsBlockMessage =>
        "Set your projects root first — that's where dzl creates and manages your mod source folders.";

    /// <summary>The always-live work-drive source folder P: is mounted from / junctions are anchored on:
    /// the explicit config override if set, else auto-derived from DayZ Tools settings.ini
    /// (<c>[ProjectDrive] path=</c>). Null → falls back to the P:\ junction path.</summary>
    private string? WorkDriveSource => EnvDetect.WorkDriveSource(_cfg.WorkDriveSource, _cfg.DayzToolsPath);

    /// <summary>Effective work-drive source for display in Settings (or a hint when not resolvable).</summary>
    public string ResolvedWorkDriveSource => WorkDriveSource ?? "(not detected — set the DayZ Tools path)";

    /// <summary>Effective keys folder shown in Settings — the override or the <c>&lt;ProjectsRoot&gt;\keys</c> default.</summary>
    public string ResolvedKeysDir => ProjectPaths.KeysDir(ProjectsRoot, _cfg.KeysDir);

    /// <summary>Effective signing-key name shown in Settings — the config value, else the cached author.</summary>
    public string ResolvedSigningKey
    {
        get
        {
            var n = !string.IsNullOrWhiteSpace(_cfg.SigningKey) ? _cfg.SigningKey.Trim() : CachedAuthor;
            return string.IsNullOrWhiteSpace(n) ? "(none — set a name or author)" : n;
        }
    }

    /// <summary>Re-enumerate mod source projects. Called on My Mods page show + after create/import/link.</summary>
    public void RefreshModProjects()
    {
        ModProjects.Clear();
        foreach (var p in Dzl.Core.Projects.ModProjects.Discover(ProjectsRoot, WorkDriveSource)) ModProjects.Add(new ModProjectVm(p));
        OnPropertyChanged(nameof(ProjectsRoot));
        _ = LoadGitStatusesAsync();   // fire-and-forget; fills each card's git badge off the UI thread
    }

    /// <summary>Fill each project card's git summary off the UI thread (git status shells out).</summary>
    private async Task LoadGitStatusesAsync()
    {
        var root = ProjectsRoot;
        foreach (var vm in ModProjects.ToList())
        {
            var dir = ProjectPaths.ModDir(root, vm.Name);
            var s = await Task.Run(() => Git.Status(dir));
            if (!s.IsRepo) { vm.Git = "no repo"; vm.RepoUrl = null; continue; }
            var ab = (s.Ahead > 0 || s.Behind > 0) ? $" ↑{s.Ahead}↓{s.Behind}" : "";
            var local = s.HasRemote ? "" : " (local)";
            vm.Git = $"{s.Branch} • {s.Detail}{ab}{local}";
            vm.RepoUrl = s.HasRemote ? await Task.Run(() => Git.RemoteUrl(dir)) : null;
        }
    }

    /// <summary>Scaffold a new mod project + link P:\&lt;Mod&gt;. Caches the author. Optionally initialises a
    /// local git repo with a first commit. Returns a status line.</summary>
    public string CreateModProject(string name, string author, bool initGit = false)
    {
        var root = ProjectsRoot;
        var res = ModScaffold.Scaffold(root, name, author);
        if (!res.Ok) return $"✗ {res.Message}";
        if (!string.IsNullOrWhiteSpace(author)) ModScaffold.SaveAuthor(ConfigDir, author);
        var dir = ProjectPaths.ModDir(root, name);
        var link = Junction.Ensure(ProjectPaths.JunctionPath(WorkDriveSource, name), dir);
        var msg = link.Ok ? $"✓ created {name} + linked P:\\{name}" : $"✓ created {name}  (⚠ P:\\ link: {link.Detail})";
        if (initGit)
        {
            var gi = Dzl.Core.Vcs.Git.Init(dir);
            if (gi.ok) Dzl.Core.Vcs.Git.CommitAll(dir, "Initial commit (dzl scaffold)");
            msg += gi.ok ? "  + git repo" : $"  (⚠ git: {gi.msg})";
        }
        RefreshModProjects();
        return msg;
    }

    /// <summary>Clone a GitHub repo into the projects tree as a mod (folder named <paramref name="name"/> or
    /// derived from the repo), then link P:\&lt;Mod&gt;. Needs gh installed + logged in. Returns a status line.</summary>
    public string ImportFromGitHub(string repo, string? name)
    {
        repo = repo?.Trim() ?? "";
        if (repo.Length == 0) return "✗ enter a GitHub repo (owner/name or URL)";
        var modName = SanitizeModName(string.IsNullOrWhiteSpace(name) ? DeriveRepoName(repo) : name!.Trim());
        if (!ProjectPaths.IsValidName(modName)) return $"✗ couldn't derive a valid mod name — type one (letters, digits, _)";
        var root = ProjectsRoot;
        var dest = ProjectPaths.ModDir(root, modName);
        if (Directory.Exists(dest)) return $"✗ {modName} already exists";
        var clone = Dzl.Core.Vcs.GitHub.Clone(repo, dest);
        if (!clone.ok) { RefreshModProjects(); return $"✗ clone failed: {clone.msg}"; }
        var link = Junction.Ensure(ProjectPaths.JunctionPath(WorkDriveSource, modName), dest);
        RefreshModProjects();
        return link.Ok ? $"✓ imported {modName} from GitHub + linked P:\\{modName}" : $"✓ imported {modName}  (⚠ link: {link.Detail})";
    }

    /// <summary>True when gh is installed + logged in (drives the "From GitHub" tab availability).</summary>
    public bool GitHubReady => Dzl.Core.Vcs.GitHub.AuthStatus().LoggedIn;

    /// <summary>The source folder for a mod project (for the per-mod git window).</summary>
    public string ModDirOf(string name) => ProjectPaths.ModDir(ProjectsRoot, name);

    /// <summary>Publish a project to GitHub (init + commit + gh repo create) for the git window. Returns the result.</summary>
    public async Task<(bool ok, string msg)> PublishForGitAsync(string name)
    {
        var cp = _configPath;
        var r = await Task.Run(() => new RepoService(cp).Publish(name));
        RefreshModProjects();
        return (r.Ok, r.Message);
    }

    /// <summary>Cut a GitHub release for a project from the git window (full options + optional built-PBO assets).</summary>
    public async Task<(bool ok, string msg)> ReleaseForGitAsync(string name, Dzl.Core.Vcs.ReleaseOptions opts, bool attachBuiltAddons)
    {
        var cp = _configPath;
        var r = await Task.Run(() => new RepoService(cp).Release(name, opts, attachBuiltAddons));
        return (r.Ok, r.Message);
    }

    private static string DeriveRepoName(string repo)
    {
        var s = repo.TrimEnd('/');
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        return s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    private static string SanitizeModName(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var name = new string(chars).Trim('_');
        if (name.Length > 0 && !char.IsLetter(name[0])) name = "Mod_" + name;
        return name;
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
        var link = Junction.Ensure(ProjectPaths.JunctionPath(WorkDriveSource, name), ProjectPaths.ModDir(ProjectsRoot, name));
        RefreshModProjects();
        return link.Ok ? $"✓ {name}: {link.Detail}" : $"✗ {name}: {link.Detail}";
    }

    /// <summary>Delete a mod project: remove its P: source junction (link only), delete the source folder, and
    /// optionally the build output. Destructive — the caller confirms first. Returns a status line.</summary>
    public string DeleteModProject(string name, bool alsoBuild)
    {
        var root = ProjectsRoot;
        try
        {
            Junction.Remove(ProjectPaths.JunctionPath(WorkDriveSource, name));   // drop the link, never the target
            var src = ProjectPaths.ModDir(root, name);
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (alsoBuild)
            {
                var build = ProjectPaths.BuildDir(root, name);
                if (Directory.Exists(build)) Directory.Delete(build, true);
            }
        }
        catch (Exception ex) { RefreshModProjects(); return $"✗ {name}: {ex.Message}"; }
        RefreshModProjects();
        Reload();   // the mod also drops out of the library / run-list discovery
        return $"✓ deleted {name}" + (alsoBuild ? " (source + build)" : " (source)");
    }

    /// <summary>Remove a mod's work-drive junction (leaves the source folder untouched). Returns a status line.</summary>
    public string UnlinkMod(string name)
    {
        var link = ProjectPaths.JunctionPath(WorkDriveSource, name);
        try { if (Junction.IsLink(link)) Junction.Remove(link); }
        catch (Exception ex) { RefreshModProjects(); return $"✗ {name}: {ex.Message}"; }
        RefreshModProjects();
        return $"✓ unlinked {name}";
    }

    // === Build → deploy (SP2) ============================================

    /// <summary>Live AddonBuilder log for the most recent build (shown on the My Mods page).</summary>
    [ObservableProperty] private string _buildLog = "";

    /// <summary>True while a build is running — used to disable the build buttons.</summary>
    [ObservableProperty] private bool _building;

    /// <summary>Build a mod project into a PBO off the UI thread, streaming the AddonBuilder log; on
    /// success register the <c>@&lt;Mod&gt;</c> into the active server's run-list and refresh.</summary>
    public async Task BuildModAsync(string name, bool clean = false, bool binarize = true, bool sign = false, bool force = false)
    {
        if (Building) return;
        Building = true;
        BuildLog = $"▸ Building {name} (clean={clean}, binarize={binarize}, sign={sign}, force={force}) …\n";
        var configPath = _configPath;
        var result = await Task.Run(() =>
            new BuildService(configPath).Build(name, clean: clean, binarize: binarize, sign: sign,
                onLine: line => _dispatcher.BeginInvoke(() => BuildLog += line + "\n"), force: force));
        BuildLog += (result.Ok ? "\n✓ " : "\n✗ ") + result.Message + "\n";
        if (!result.Ok && result.Diagnostics.Length > 0)
            BuildLog += "\n" + result.Diagnostics + "\n";
        Building = false;
        if (result.Ok) { Reload(); RefreshModProjects(); }
    }

    /// <summary>Preflight a mod project off the UI thread (configs, references, paths, scripts).</summary>
    public Task<BuildService.PreflightView> PreflightAsync(string name)
    {
        var configPath = _configPath;
        return Task.Run(() => new BuildService(configPath).Preflight(name));
    }

    /// <summary>Resolved path/tool preview for the Build options dialog (no side effects).</summary>
    public BuildService.BuildPlanView BuildPlan(string name) => new BuildService(_configPath).Plan(name);

    /// <summary>Create the creator's signing key (DSCreateKey). Returns a status line.</summary>
    public string GenerateSigningKey()
    {
        var r = new BuildService(_configPath).GenerateKey();
        return r.Ok ? $"✓ key ready: {r.PrivateKey}" : $"✗ {r.Output}";
    }

    // === Steam Workshop (SP5) =============================================

    public ObservableCollection<WorkshopItem> WorkshopResults { get; } = new();

    /// <summary>Items subscribed in the Steam client (its content folder) — what the Launcher loads.</summary>
    public ObservableCollection<SubscribedItem> WorkshopSubscribed { get; } = new();

    /// <summary>Items downloaded manually via steamcmd into &lt;ProjectsRoot&gt;\workshop.</summary>
    public ObservableCollection<SubscribedItem> WorkshopDownloaded { get; } = new();

    /// <summary>Sort options + time frames (from the Workshop browse page) for the toolbar combos.</summary>
    public IReadOnlyList<WorkshopSort> WorkshopSorts => WorkshopWeb.Sorts;
    public IReadOnlyList<WorkshopTimeFrame> WorkshopTimeFrames => WorkshopWeb.TimeFrames;

    /// <summary>Filter tags (Type + DayZ Mod-Type categories) — toggled, AND-ed into the query.</summary>
    public ObservableCollection<WorkshopCategoryVm> WorkshopFilters { get; } = new();

    [ObservableProperty] private string _workshopQuery = "";
    [ObservableProperty] private string _workshopStatus = "";
    [ObservableProperty] private WorkshopSort? _selectedSort;
    [ObservableProperty] private WorkshopTimeFrame? _selectedTimeFrame;
    [ObservableProperty] private bool _showTimeFrame = true;
    [ObservableProperty] private WorkshopItem? _selectedWorkshopItem;
    [ObservableProperty] private WorkshopItem? _workshopDetail;
    [ObservableProperty] private bool _detailSubscribed;   // is the item shown in the details pane already subscribed?
    private int _workshopPage = 1;
    private bool _wsReady;

    /// <summary>Build the filter tag list + sort/time-frame defaults (once). Call before showing the window.</summary>
    public void InitWorkshop()
    {
        if (_wsReady) return;
        foreach (var t in WorkshopWeb.Types.Concat(WorkshopWeb.ModTypes))
        {
            var c = new WorkshopCategoryVm(t);
            c.Toggled += () => { if (_wsReady) _ = WorkshopBrowseAsync(); };
            WorkshopFilters.Add(c);
        }
        SelectedSort = WorkshopSorts[0];                 // Most Popular (partial handlers no-op until _wsReady)
        SelectedTimeFrame = WorkshopTimeFrames[1];       // One Week
        _wsReady = true;
    }

    private IEnumerable<string> SelectedTags() => WorkshopFilters.Where(f => f.Selected).Select(f => f.Name);

    partial void OnSelectedSortChanged(WorkshopSort? value)
    {
        ShowTimeFrame = value?.BrowseSort == "trend";
        if (_wsReady) _ = WorkshopBrowseAsync();
    }

    partial void OnSelectedTimeFrameChanged(WorkshopTimeFrame? value) { if (_wsReady && ShowTimeFrame) _ = WorkshopBrowseAsync(); }

    partial void OnSelectedWorkshopItemChanged(WorkshopItem? value) => _ = LoadDetailAsync(value);

    partial void OnWorkshopDetailChanged(WorkshopItem? value) => RecomputeDetailSubscribed();

    /// <summary>Refresh <see cref="DetailSubscribed"/> from the current detail item + subscribed list.</summary>
    private void RecomputeDetailSubscribed()
        => DetailSubscribed = WorkshopDetail is { } d && WorkshopSubscribed.Any(s => s.Id == d.Id);

    /// <summary>Reload the subscribed-items list (Steam client content folder).</summary>
    public void RefreshSubscribed()
    {
        var svc = new WorkshopService(_configPath);
        WorkshopSubscribed.Clear();
        foreach (var s in svc.Subscribed()) WorkshopSubscribed.Add(s);
        WorkshopDownloaded.Clear();
        foreach (var s in svc.DownloadedItems()) WorkshopDownloaded.Add(s);
        RecomputeDetailSubscribed();
    }

    /// <summary>Browse with the current sort + time frame + selected category tags + search (page 1).</summary>
    public async Task WorkshopBrowseAsync()
    {
        _workshopPage = 1;
        var sort = SelectedSort?.BrowseSort ?? "trend";
        var days = ShowTimeFrame ? (SelectedTimeFrame?.Days ?? 7) : 0;
        var tags = SelectedTags().ToList();
        WorkshopStatus = "loading…";
        var (ok, error, items) = await new WorkshopService(_configPath).BrowseAsync(sort, days, WorkshopQuery, 30, 1, tags);
        WorkshopResults.Clear();
        foreach (var it in items) WorkshopResults.Add(it);
        SelectedWorkshopItem = WorkshopResults.FirstOrDefault();
        var f = tags.Count > 0 ? " · " + string.Join("+", tags) : "";
        WorkshopStatus = ok ? $"{items.Count} result(s){f}" : $"✗ {error}";
    }

    /// <summary>Append the next page of the current browse.</summary>
    public async Task WorkshopLoadMoreAsync()
    {
        _workshopPage++;
        var sort = SelectedSort?.BrowseSort ?? "trend";
        var days = ShowTimeFrame ? (SelectedTimeFrame?.Days ?? 7) : 0;
        WorkshopStatus = "loading more…";
        var (ok, error, items) = await new WorkshopService(_configPath).BrowseAsync(sort, days, WorkshopQuery, 30, _workshopPage, SelectedTags().ToList());
        foreach (var it in items) WorkshopResults.Add(it);
        WorkshopStatus = ok ? $"{WorkshopResults.Count} total (page {_workshopPage})" : $"✗ {error}";
    }

    /// <summary>True when a Steam session is stored (signed in).</summary>
    public bool SteamSignedIn => new WorkshopService(_configPath).SignedIn;

    /// <summary>True when steamcmd is configured + present (drives the Workshop Download gating).</summary>
    public bool SteamCmdConfigured => !string.IsNullOrWhiteSpace(_cfg.SteamCmdPath) && File.Exists(_cfg.SteamCmdPath);

    /// <summary>Re-evaluate the Workshop page's gating flags (sign-in / steamcmd) after a change.</summary>
    public void NotifyWorkshopGate()
    {
        OnPropertyChanged(nameof(SteamSignedIn));
        OnPropertyChanged(nameof(SteamCmdConfigured));
    }

    /// <summary>Steam account label for the Settings → Accounts row (reflects the stored sign-in).</summary>
    [ObservableProperty] private string _steamAccount = "not signed in";

    /// <summary>Refresh the Steam account label from the stored session + the account name saved on sign-in.</summary>
    public void RefreshSteamAccount()
        => SteamAccount = SteamSignedIn
            ? (string.IsNullOrWhiteSpace(Cfg.SteamLogin) ? "signed in" : $"logged in as {Cfg.SteamLogin}")
            : "not signed in";

    public Task<Dzl.Core.Workshop.SteamLoginResult> SteamLoginQrAsync(Action<string> onUrl, System.Threading.CancellationToken ct)
        => new WorkshopService(_configPath).LoginViaQrAsync(onUrl, ct);

    public Task<Dzl.Core.Workshop.SteamLoginResult> SteamLoginCredentialsAsync(string user, string pass, SteamKit2.Authentication.IAuthenticator auth, System.Threading.CancellationToken ct)
        => new WorkshopService(_configPath).LoginViaCredentialsAsync(user, pass, auth, ct);

    public void SteamSignOut() => new WorkshopService(_configPath).SignOut();

    /// <summary>Subscribe in-app via the Steam web token if set; returns false (so the caller opens the Steam
    /// page) when no token is configured.</summary>
    public async Task<bool> SubscribeWorkshopAsync(string id)
    {
        var svc = new WorkshopService(_configPath);
        if (!svc.HasAccessToken) return false;
        WorkshopStatus = "subscribing…";
        var (ok, msg) = await svc.SubscribeAsync(id, true);
        WorkshopStatus = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
        {
            RefreshSubscribed();   // reflect the Steam client folder (may lag — download is async)
            // Optimistic: show it as subscribed immediately even before Steam finishes downloading it.
            if (!WorkshopSubscribed.Any(s => s.Id == id))
            {
                var title = WorkshopResults.FirstOrDefault(r => r.Id == id)?.Title
                            ?? (WorkshopDetail?.Id == id ? WorkshopDetail!.Title : null) ?? id;
                WorkshopSubscribed.Insert(0, new SubscribedItem(id, $"{title}  (downloading…)", ""));
            }
            RecomputeDetailSubscribed();
        }
        return true;
    }

    /// <summary>Unsubscribe a mod (needs a Steam session); removes it from the Subscribed list on success.</summary>
    public async Task UnsubscribeWorkshopAsync(string id)
    {
        var svc = new WorkshopService(_configPath);
        if (!svc.HasAccessToken) { WorkshopStatus = "✗ sign in to Steam to unsubscribe (Settings → Steam)"; return; }
        WorkshopStatus = "unsubscribing…";
        var (ok, msg) = await svc.SubscribeAsync(id, subscribe: false);
        WorkshopStatus = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
        {
            var it = WorkshopSubscribed.FirstOrDefault(s => s.Id == id);
            if (it is not null) WorkshopSubscribed.Remove(it);
            RecomputeDetailSubscribed();
        }
    }

    /// <summary>Show an item's details in the right pane by id (e.g. from the Subscribed/Downloaded lists).
    /// Selects it in the results when present (which loads details), otherwise fetches details directly.</summary>
    public async Task ShowDetailAsync(string id)
    {
        var inResults = WorkshopResults.FirstOrDefault(r => r.Id == id);
        if (inResults is not null) { SelectedWorkshopItem = inResults; return; }
        WorkshopDetail = new WorkshopItem(id, id);   // placeholder until the fetch lands
        var full = await new WorkshopService(_configPath).DetailsAsync(id);
        if (full is not null) WorkshopDetail = full;
    }

    // Show the list item immediately in the details pane, then enrich (subs/description/tags) keylessly.
    private async Task LoadDetailAsync(WorkshopItem? item)
    {
        WorkshopDetail = item;
        if (item is null) return;
        var full = await new WorkshopService(_configPath).DetailsAsync(item.Id);
        if (full is not null && SelectedWorkshopItem?.Id == item.Id) WorkshopDetail = full;
    }

    /// <summary>Auto-install steamcmd into the config dir; returns (ok, exe path, message).</summary>
    public Task<(bool ok, string path, string error)> InstallSteamCmdAsync()
        => InstallSteamCmdCore(Path.Combine(ConfigDir, "steamcmd"));

    private static async Task<(bool ok, string path, string error)> InstallSteamCmdCore(string dest)
    {
        var (ok, exe, msg) = await SteamCmdInstaller.InstallAsync(dest);
        return (ok, exe, msg);
    }

    /// <summary>Download a Workshop item by id via steamcmd (opens a console). Returns a status line.</summary>
    public string WorkshopDownload(string id)
    {
        var r = new WorkshopService(_configPath).Download(id);
        WorkshopStatus = (r.Ok ? "✓ " : "✗ ") + r.Message;
        return WorkshopStatus;
    }

    /// <summary>Where item <paramref name="id"/> actually lives on disk (Steam client folder or the steamcmd
    /// download under ProjectsRoot), or null if it isn't downloaded yet.</summary>
    public string? ResolveModFolder(string id) => new WorkshopService(_configPath).ResolveContentDir(id);

    /// <summary>Delete a steamcmd-downloaded item (junction + cached files), then refresh the lists.</summary>
    public string DeleteDownloaded(string id)
    {
        var (ok, msg) = new WorkshopService(_configPath).DeleteDownloaded(id);
        WorkshopStatus = (ok ? "✓ " : "✗ ") + msg;
        if (ok) RefreshSubscribed();
        return WorkshopStatus;
    }

    // === Code editor ======================================================

    /// <summary>True when a code editor is configured (drives the "Open in editor" buttons).</summary>
    public bool HasEditor => !string.IsNullOrWhiteSpace(_cfg.EditorPath);

    /// <summary>Open a folder (mod project or server instance) in the configured editor. Returns a status.</summary>
    public string OpenInEditor(string folder)
    {
        if (!HasEditor) return "✗ no editor set — Settings → Editor → Detect";
        return EditorLauncher.Open(_cfg.EditorPath, folder)
            ? $"✓ opened {Path.GetFileName(folder.TrimEnd('\\', '/'))} in editor"
            : "✗ could not launch the editor";
    }

    /// <summary>Detected editors on this machine (for the Settings Detect button).</summary>
    public List<EditorInfo> DetectEditors() => EditorDetect.Detect();


    /// <summary>GitHub account label for the Settings → Accounts row (keyless; reflects gh's OAuth login).</summary>
    [ObservableProperty] private string _ghAccount = "checking…";

    /// <summary>Whether gh reports a logged-in account (drives Login/Logout button enablement).</summary>
    [ObservableProperty] private bool _ghLoggedIn;

    /// <summary>Refresh the GitHub auth label off the UI thread (gh auth status shells out).</summary>
    public async Task RefreshGitHubAuthAsync()
    {
        var a = await Task.Run(() => GitHub.AuthStatus());
        GhLoggedIn = a.LoggedIn;
        GhAccount = a.Detail;
    }

    /// <summary>Log out of GitHub (gh auth logout), then refresh the label.</summary>
    public async Task GitHubLogoutAsync()
    {
        await Task.Run(() => GitHub.Logout());
        await RefreshGitHubAuthAsync();
    }


    // --- CE Dictionaries manager ------------------------------------------
    // The Dictionaries tab edits cfglimitsdefinition.xml (the four base lists) + cfglimitsdefinitionuser.xml
    // (named combos). It shares the SAME limit names the Types editor suggests + lints against, so after any
    // dictionary edit we re-pull those (RefreshLimitsFromDisk) and re-lint — removing a value flags types that
    // used it; adding one clears false "unknown" warnings.

    private Dzl.Tray.Controls.DictionaryManagerVm? _dictionaries;

    /// <summary>Backs the Dictionaries tab. Created lazily on first access so it shares this VM's config path
    /// and can call back into the Types editor (suggestions + lint) after every dictionary edit.</summary>
    public Dzl.Tray.Controls.DictionaryManagerVm Dictionaries =>
        _dictionaries ??= new Dzl.Tray.Controls.DictionaryManagerVm(
            _configPath,
            onDictionaryChanged: TypesEditor.RefreshLimitsFromDisk,
            usageCount: TypesEditor.CountTypesUsing,
            confirm: ConfirmDictionaryAction);

    /// <summary>(Re)load the Dictionaries tab from disk. Called when the Economy page is shown.</summary>
    public void RefreshDictionaries() => Dictionaries.Reload();

    // --- CE Random Presets tab (cfgrandompresets.xml) -----------------------
    private Dzl.Tray.Controls.RandomPresetsVm? _randomPresets;

    /// <summary>Backs the Random Presets tab (cargo/attachments presets + items). Created lazily so it
    /// shares this VM's config path.</summary>
    public Dzl.Tray.Controls.RandomPresetsVm RandomPresets =>
        _randomPresets ??= new Dzl.Tray.Controls.RandomPresetsVm(_configPath, ConfirmDictionaryAction);

    /// <summary>(Re)load the Random Presets tab from disk. Called when its tab is activated.</summary>
    public void RefreshRandomPresets() => RandomPresets.Reload();

    // --- CE Events tab (db/events.xml) -------------------------------------
    private Dzl.Tray.Controls.EventsVm? _events;

    /// <summary>Backs the Events tab (CE spawn events + children). Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.EventsVm Events =>
        _events ??= new Dzl.Tray.Controls.EventsVm(_configPath, ConfirmDictionaryAction);

    /// <summary>(Re)load the Events tab from disk. Called when its tab is activated.</summary>
    public void RefreshEvents() => Events.Reload();

    // --- CE Globals tab (db/globals.xml) ------------------------------------
    private Dzl.Tray.Controls.GlobalsVm? _globals;

    /// <summary>Backs the Globals tab (simulation vars). Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.GlobalsVm Globals =>
        _globals ??= new Dzl.Tray.Controls.GlobalsVm(_configPath, ConfirmDictionaryAction);

    /// <summary>(Re)load the Globals tab from disk. Called when its tab is activated.</summary>
    public void RefreshGlobals() => Globals.Reload();

    // --- CE Spawnable Types tab (cfgspawnabletypes.xml) ---------------------
    private Dzl.Tray.Controls.SpawnableTypesVm? _spawnableTypes;

    /// <summary>Backs the Spawnable Types tab (per-type hoarder/damage + cargo/attachments blocks). Created
    /// lazily so it shares this VM's config path; its preset dropdowns read cfgrandompresets.xml.</summary>
    public Dzl.Tray.Controls.SpawnableTypesVm SpawnableTypes =>
        _spawnableTypes ??= new Dzl.Tray.Controls.SpawnableTypesVm(_configPath, ConfirmDictionaryAction);

    /// <summary>(Re)load the Spawnable Types tab from disk. Called when its tab is activated.</summary>
    public void RefreshSpawnableTypes() => SpawnableTypes.Reload();

    // --- CE Player Spawns tab (cfgplayerspawnpoints.xml) --------------------
    private Dzl.Tray.Controls.PlayerSpawnsVm? _playerSpawns;

    /// <summary>Backs the Player Spawns tab (fresh/hop/travel categories, their param bags + position groups).
    /// Created lazily so it shares this VM's config path.</summary>
    public Dzl.Tray.Controls.PlayerSpawnsVm PlayerSpawns =>
        _playerSpawns ??= new Dzl.Tray.Controls.PlayerSpawnsVm(_configPath, ConfirmDictionaryAction);

    /// <summary>(Re)load the Player Spawns tab from disk. Called when its tab is activated.</summary>
    public void RefreshPlayerSpawns() => PlayerSpawns.Reload();

    private static bool ConfirmDictionaryAction(string message) =>
        System.Windows.MessageBox.Show(message, "Dictionaries",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
        == System.Windows.MessageBoxResult.Yes;

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
    /// <paramref name="baseName"/> = a base/template to copy from, or null for the DayZ install.
    /// Returns a status line.</summary>
    public string CreateServer(string name, string map, int? port, string? baseName = null)
    {
        var res = new ServerService(_configPath).Create(name, map, port, activate: true, baseName: baseName);
        Reload();              // active preset changed → refresh mods/paths/preset list
        RefreshServers();
        return res.Ok ? $"✓ {res.Message}  (port {res.Port})" : $"✗ {res.Message}";
    }

    // === Bases (templates) ================================================

    /// <summary>Sentinel for "use the DayZ install" in the New-server base dropdown.</summary>
    public const string VanillaChoice = "DayZ install (vanilla)";

    /// <summary>Discovered bases (cards on the Bases page).</summary>
    public ObservableCollection<BaseInfo> Bases { get; } = new();

    /// <summary>Base choices for the New-server dropdown: the vanilla sentinel + each base name.</summary>
    public ObservableCollection<string> BaseChoices { get; } = new();

    public void RefreshBases()
    {
        var list = ServerBases.List(ProjectsRoot);
        Bases.Clear();
        foreach (var b in list) Bases.Add(b);
        BaseChoices.Clear();
        BaseChoices.Add(VanillaChoice);
        foreach (var b in list) BaseChoices.Add(b.Name);
        OnPropertyChanged(nameof(ProjectsRoot));
    }

    public string CreateBaseFromInstall(string name, string map)
    {
        var (ok, msg) = ServerBases.CreateFromInstall(ProjectsRoot, name, _cfg.DayzPath, MapAliases.MissionTemplate(map));
        RefreshBases();
        return (ok ? "✓ " : "✗ ") + msg;
    }

    public string CreateEmptyBase(string name)
    {
        var (ok, msg) = ServerBases.CreateEmpty(ProjectsRoot, name);
        RefreshBases();
        return (ok ? "✓ " : "✗ ") + msg;
    }

    public string DeleteBase(string name)
    {
        var ok = ServerBases.Delete(ProjectsRoot, name);
        RefreshBases();
        return ok ? $"✓ deleted base '{name}'" : $"✗ no base '{name}'";
    }

    /// <summary>The folder of a base (for Open-folder).</summary>
    public string BaseDirOf(string name) => ServerBases.BaseDir(ProjectsRoot, name);

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

    /// <summary>Persist edited per-server settings to the active instance, then reload.</summary>
    public void SaveActiveInstance(DzlConfig edited)
    {
        Profiles.Save(edited, ActiveName, _configPath);
        Reload();
        RefreshServers();
    }

    /// <summary>Delete a server instance. If it was active, fall back to another (or a fresh default).</summary>
    public string DeleteServer(string name, bool removeFiles = false)
    {
        var wasActive = ActivePreset == name;
        if (!Profiles.Delete(name, _configPath, removeFiles)) return $"✗ no server '{name}'";
        if (wasActive)
        {
            // Fall back to another instance so something stays active (else seed a fresh default).
            var remaining = Profiles.List(_configPath);
            if (remaining.Count > 0) Profiles.SetActive(remaining[0], _configPath);
            else { Profiles.SetActive("", _configPath); Profiles.EnsureDefault(_configPath); }
        }
        Reload();
        RefreshServers();
        return removeFiles ? $"✓ deleted '{name}' + its files" : $"✓ deleted '{name}' (files kept on disk)";
    }

    /// <summary>Clone the active instance's config to a new name and activate it.</summary>
    public string CloneActive(string newName)
    {
        if (!ProjectPaths.IsValidName(newName)) return $"✗ invalid name: {newName}";
        if (Profiles.List(_configPath).Contains(newName)) return $"✗ '{newName}' already exists";
        Profiles.Save(_cfg, newName, _configPath);    // _cfg = the active composed config
        Profiles.SetActive(newName, _configPath);
        Reload();
        RefreshServers();
        return $"✓ cloned active → '{newName}' (now active)";
    }

    /// <summary>The active server's folder (where its serverDZ.cfg / mpmissions / profiles live).</summary>
    public string ActiveServerDir =>
        Path.IsPathRooted(_cfg.ConfigName)
            ? Path.GetDirectoryName(_cfg.ConfigName) ?? ProjectPaths.ServerDir(ProjectsRoot, ActiveName)
            : ProjectPaths.ServerDir(ProjectsRoot, ActiveName);

    /// <summary>Delete the active server's Central Economy persistence (storage_*) so the next start
    /// regenerates it fresh. Returns a status line.</summary>
    public string WipeActivePersistence() => WipePersistenceDir(ActiveServerDir);

    /// <summary>Wipe persistence (storage_*) for the server whose files live in <paramref name="dir"/>.</summary>
    public string WipePersistenceDir(string dir)
    {
        var n = ServerScaffold.WipePersistence(dir);
        return n > 0
            ? $"✓ wiped {n} storage folder(s) — fresh persistence on next start"
            : "nothing to wipe (persistence is already clean)";
    }

    /// <summary>Rename the active instance (copy → new name, delete old, activate new).</summary>
    public string RenameActive(string newName)
    {
        var old = ActiveName;
        if (!ProjectPaths.IsValidName(newName)) return $"✗ invalid name: {newName}";
        if (string.Equals(newName, old, StringComparison.OrdinalIgnoreCase)) return "✗ same name";
        if (Profiles.List(_configPath).Contains(newName)) return $"✗ '{newName}' already exists";
        Profiles.Save(_cfg, newName, _configPath);
        Profiles.Delete(old, _configPath);
        Profiles.SetActive(newName, _configPath);
        Reload();
        RefreshServers();
        return $"✓ renamed '{old}' → '{newName}'";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusTimer.Stop();
        TypesEditor.Dispose();
        _cts.Cancel();
        _cts.Dispose();
        _logCts.Cancel();
        _logCts.Dispose();
    }
}
