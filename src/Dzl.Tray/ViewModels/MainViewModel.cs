using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Ipc;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Mods;

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
    private const int MaxLogLines = 500;

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
    [ObservableProperty] private string _serverArgv = "";
    [ObservableProperty] private string _clientArgv = "";
    [ObservableProperty] private string _activePreset = "";
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _selectedPreset = "";
    [ObservableProperty] private string _modFilter = "";
    [ObservableProperty] private ModRowVm? _selectedMod;

    [ObservableProperty] private string _scriptLog = "";
    [ObservableProperty] private string _rptLog = "";
    [ObservableProperty] private string _admLog = "";
    [ObservableProperty] private string _clientLog = "";

    public ObservableCollection<ModRowVm> Mods { get; } = new();
    public ObservableCollection<string> Presets { get; } = new();

    /// <summary>Snapshot of the current config (for the Settings/Params dialogs).</summary>
    public DzlConfig Cfg => _cfg;

    /// <summary>The active profile save path (where edits persist).</summary>
    public string SavePath => _savePath;

    /// <summary>The config path the window/VM resolved (for opening folders, presets).</summary>
    public string ConfigFilePath => _configPath;

    /// <summary>Filtered view over <see cref="Mods"/> bound by the ListBox; reorder
    /// commands still mutate the underlying collection.</summary>
    public ICollectionView ModsView { get; }

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
        // Reorder commands mutate the ObservableCollection via Move(); refresh the
        // filtered view so its ordering stays in sync with the underlying list.
        ModsView.Refresh();
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

    [RelayCommand]
    private void SwitchPreset()
    {
        var name = SelectedPreset;
        if (string.IsNullOrEmpty(name) || name == ActivePreset) return;
        try
        {
            new ControlPlane(_configPath).SetPresetJson(name);
            Reload();
        }
        catch { /* ignore; status keeps polling */ }
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = string.IsNullOrWhiteSpace(NewPresetName) ? ActivePreset : NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        new ControlPlane(_configPath).SavePresetJson(name);
        NewPresetName = "";
        Reload();
    }

    /// <summary>Switch to a named preset (used by the menu's per-preset items).</summary>
    public void SwitchToPreset(string name)
    {
        if (string.IsNullOrEmpty(name) || name == ActivePreset) return;
        try
        {
            new ControlPlane(_configPath).SetPresetJson(name);
            Reload();
        }
        catch { /* ignore; status keeps polling */ }
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
        try
        {
            LoadMods();
            LoadPresets();
        }
        finally { _suppressPersist = false; }
        RefreshPreview();
    }

    // --- Live log tailing --------------------------------------------------

    private void StartLogTails()
    {
        var paths = LogResolver.Resolve(_cfg.ProfilesPath, _cfg.ClientProfilesPath);
        Tail(paths.GetValueOrDefault("script"), s => ScriptLog = s, () => ScriptLog);
        Tail(paths.GetValueOrDefault("rpt"), s => RptLog = s, () => RptLog);
        Tail(paths.GetValueOrDefault("adm"), s => AdmLog = s, () => AdmLog);
        Tail(paths.GetValueOrDefault("client"), s => ClientLog = s, () => ClientLog);
    }

    private void Tail(string? path, Action<string> setter, Func<string> getter)
    {
        if (string.IsNullOrEmpty(path)) return;
        // Seed with the existing tail so the pane isn't empty.
        foreach (var line in LogTail.LastLines(path, 200)) Append(setter, getter, line);
        _ = LogTail.Follow(path, line => _dispatcher.BeginInvoke(() => Append(setter, getter, line)), _cts.Token);
    }

    private static void Append(Action<string> setter, Func<string> getter, string line)
    {
        var text = getter();
        text = text.Length == 0 ? line : text + "\n" + line;
        // Cap to ~MaxLogLines to keep memory bounded.
        var nl = CountLines(text);
        if (nl > MaxLogLines)
        {
            var idx = 0;
            var drop = nl - MaxLogLines;
            for (int i = 0; i < drop; i++)
            {
                idx = text.IndexOf('\n', idx);
                if (idx < 0) break;
                idx++;
            }
            if (idx > 0) text = text[idx..];
        }
        setter(text);
    }

    private static int CountLines(string s)
    {
        if (s.Length == 0) return 0;
        var n = 1;
        foreach (var c in s) if (c == '\n') n++;
        return n;
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
