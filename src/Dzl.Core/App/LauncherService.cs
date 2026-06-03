using Dzl.Core.Config;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Mods;

namespace Dzl.Core.App;

public sealed record TargetState(string State, string? Source, string? Mode, int? Pid);
public sealed record StatusReport(
    string Mode, int Port, string? ActivePreset,
    TargetState Server, TargetState Client,
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyList<ModView> Mods,
    IReadOnlyDictionary<string, string?> Logs);
public sealed record ModView(string Path, string Side);
public sealed record PresetView(string Name, bool Active);
public sealed record LogsResult(string Which, string? Path, IReadOnlyList<string> Lines);
public sealed record OpResult(bool Ok, string Message);

public sealed class LauncherService
{
    private readonly string _configPath;
    public LauncherService(string configPath) { _configPath = configPath; }

    private (DzlConfig cfg, string savePath, string active) Resolve()
    {
        Profiles.EnsureDefault(_configPath);
        return Profiles.ResolveActive(_configPath);
    }

    private TargetState TargetOf(IReadOnlyDictionary<string, ProcInfo> live, string t)
        => live.TryGetValue(t, out var i)
            ? new TargetState("up", i.Source, i.Mode, i.Pid)
            : new TargetState("down", null, null, null);

    public StatusReport Status()
    {
        var (cfg, _, active) = Resolve();
        var live = StateFile.ReadLive(_configPath, ProcessManager.ImageOf);
        var logs = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath);
        var paths = new Dictionary<string, string>
        {
            ["dayz_path"] = cfg.DayzPath,
            ["profiles_path"] = cfg.ProfilesPath,
            ["client_profiles_path"] = cfg.ClientProfilesPath,
            ["config_dir"] = Path.GetDirectoryName(_configPath) ?? ".",
            ["presets_dir"] = Profiles.PresetsDir(_configPath),
        };
        var mods = cfg.Mods.Where(m => m.Enabled).Select(m => new ModView(m.Path, m.Side)).ToList();
        return new StatusReport(cfg.Mode, cfg.Port, string.IsNullOrEmpty(active) ? null : active,
            TargetOf(live, "server"), TargetOf(live, "client"), paths, mods, logs);
    }

    public IReadOnlyList<ModView> Mods()
    {
        var (cfg, _, _) = Resolve();
        return cfg.Mods.Where(m => m.Enabled).Select(m => new ModView(m.Path, m.Side)).ToList();
    }

    public IReadOnlyList<PresetView> Presets()
    {
        var (_, _, active) = Resolve();
        return Profiles.List(_configPath).Select(n => new PresetView(n, n == active)).ToList();
    }

    public OpResult SetPreset(string name)
    {
        if (!Profiles.List(_configPath).Contains(name)) return new OpResult(false, $"no preset '{name}'");
        Profiles.SetActive(name, _configPath);
        return new OpResult(true, $"active preset -> '{name}'");
    }

    public OpResult SaveActivePresetAs(string name)
    {
        var (cfg, _, _) = Resolve();
        Profiles.Save(cfg, name, _configPath);
        Profiles.SetActive(name, _configPath);
        return new OpResult(true, $"saved & active: '{name}'");
    }

    public LogsResult Logs(string which, int lines)
    {
        var (cfg, _, _) = Resolve();
        var path = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath).GetValueOrDefault(which);
        var tail = path is null ? new List<string>() : LogTail.LastLines(path, lines);
        return new LogsResult(which, path, tail);
    }

    public OpResult Start(string mode, bool client, string source = "cli")
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Spawn(mode, "server", cfg, source, _configPath);
        if (client) ProcessManager.Spawn(mode, "client", cfg, source, _configPath);
        return new OpResult(true, $"started server{(client ? " + client" : "")} ({mode})");
    }

    public OpResult Stop(bool client, string source = "cli")  // source unused by Stop; keep for symmetry
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Stop("server", cfg, _configPath);
        if (client) ProcessManager.Stop("client", cfg, _configPath);
        return new OpResult(true, $"stopped server{(client ? " + client" : "")}");
    }

    public OpResult Restart(string mode, string source = "cli")
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Restart(mode, cfg, _configPath, source);
        return new OpResult(true, $"restarted server ({mode})");
    }

    public OpResult StartTarget(string target, string mode, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Spawn(mode, target, cfg, source, _configPath);
        return new OpResult(true, $"started {target} ({mode})");
    }

    public OpResult StopTarget(string target, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Stop(target, cfg, _configPath);
        return new OpResult(true, $"stopped {target}");
    }

    public OpResult RestartTarget(string target, string mode, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        if (target == "server")
            ProcessManager.Restart(mode, cfg, _configPath, source);
        else
        {
            ProcessManager.Stop(target, cfg, _configPath);
            ProcessManager.Spawn(mode, target, cfg, source, _configPath);
        }
        return new OpResult(true, $"restarted {target} ({mode})");
    }
}
