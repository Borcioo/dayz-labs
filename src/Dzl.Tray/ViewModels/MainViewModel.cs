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
using Dzl.Core.Tools;
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
    private readonly string _savePath;
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
        var (cfg, _, active) = Profiles.ResolveActive(_configPath);
        _cfg = cfg;
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
        Task.Run(() =>
        {
            try { WorkDrive.Mount(exe); } catch { }
            finally { _dispatcher.BeginInvoke(RefreshWorkDrive); }
        });
    }

    public void UnmountWorkDrive() => Task.Run(() =>
    {
        try { WorkDrive.Unmount(); } catch { }
        finally { _dispatcher.BeginInvoke(RefreshWorkDrive); }
    });

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusTimer.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
