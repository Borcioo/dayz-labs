using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Tray.ViewModels;

/// <summary>
/// Backs <c>MainWindow</c>: a mod checklist (reorderable, side-cycle), live status
/// line, argv preview, profile switcher and four live log panes. Edits persist to the
/// active profile; server/client ops go through the tray's own <see cref="LauncherService"/>
/// (the tray process is the IPC authority, so the window calls it directly rather than
/// routing back through a pipe).
///
/// The class is split across <c>MainViewModel.&lt;Area&gt;.cs</c> partials by page/area
/// (Mods, Lifecycle, Presets, Logs, Tools, MyMods, Workshop, Settings, Economy, Servers,
/// Bases). This file holds the shell: shared fields, ctor, status poll, mode, the MCP page
/// surface, <see cref="Reload"/>, and <see cref="Dispose"/>.
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
    // Guards the status poll: the tick fires every 1.5s, but Status() spawns a tasklist per
    // recorded PID and can outlast the interval — without this a slow poll would stack up.
    private bool _statusBusy;

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

    /// <summary>True when <see cref="Mode"/> is "normal" (drives the mode ToggleSwitch).</summary>
    public bool IsNormalMode => Mode == "normal";

    partial void OnModeChanged(string value) => OnPropertyChanged(nameof(IsNormalMode));

    /// <summary>Snapshot of the current config (for the Settings/Params dialogs).</summary>
    public DzlConfig Cfg => _cfg;

    /// <summary>The active profile save path (where edits persist).</summary>
    public string SavePath => _savePath;

    /// <summary>The config path the window/VM resolved (for opening folders, presets).</summary>
    public string ConfigFilePath => _configPath;

    public MainViewModel(string configPath)
    {
        _configPath = configPath;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _svc = new LauncherService(configPath);
        AutomationOn = App.AutomationServerRunning;
        McpRegisterCommand =
            $"claude mcp add dzl --env DZL_CONFIG=\"{configPath}\" -- {McpLauncher.Resolve(AppContext.BaseDirectory, File.Exists)}";

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
        _statusTimer.Tick += (_, _) => { _ = RefreshStatusAsync(); RefreshLogPaths(); RefreshWorkDrive(); };
        _statusTimer.Start();
        _ = RefreshStatusAsync();

        // Seed the GitHub/Steam pills once at startup (gh shells out — keep it off the UI thread;
        // login/logout flows re-run these on their own).
        RefreshSteamAccount();
        _ = RefreshGitHubAuthAsync();

        StartLogTails();
    }

    /// <summary>Poll launcher status off the UI thread. <see cref="LauncherService.Status"/> spawns a
    /// <c>tasklist</c> per recorded PID, so the work runs on a background thread and only the property
    /// assignments hop back. Re-entrancy is guarded by <see cref="_statusBusy"/> (a slow poll can outlast
    /// the 1.5s tick); the cached <see cref="_svc"/> is reused instead of allocating per tick.</summary>
    private async Task RefreshStatusAsync()
    {
        if (_statusBusy) return;
        _statusBusy = true;
        try
        {
            var r = await Task.Run(() => _svc.Status());
            if (_disposed) return;
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
            if (!_disposed) StatusLine = "status error: " + ex.Message;
        }
        finally { _statusBusy = false; }
    }

    /// <summary>Copy the <c>claude mcp add</c> registration command to the clipboard
    /// (clipboard access can throw if another process holds it; swallow best-effort).</summary>
    [RelayCommand]
    private void CopyMcpCommand()
    {
        try { System.Windows.Clipboard.SetText(McpRegisterCommand); }
        catch { /* clipboard busy/unavailable — best-effort */ }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusTimer.Stop();
        TypesEditor.Dispose();
        _cts.Cancel();
        _cts.Dispose();
        // Cancel only — same rule as RetailLogs: Follow tasks may still hold the token, and
        // disposing under them races into ObjectDisposedException. GC collects the CTS.
        _logCts.Cancel();
    }
}
