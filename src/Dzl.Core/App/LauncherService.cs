using Dzl.Core.Config;
using Dzl.Core.Economy;
using Dzl.Core.Env;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Projects;

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
            ["projects_root"] = ProjectPaths.Root(cfg),
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

    /// <summary>CLI/MCP starts pull the tray up as a monitor when configured; the tray's own
    /// starts ("tui") never re-launch it.</summary>
    private static void AutoLaunchTrayIfWanted(DzlConfig cfg, string source)
    {
        if (source is "cli" or "mcp" && cfg.AutoLaunchTray && !TrayLauncher.IsTrayRunning())
            TrayLauncher.LaunchMonitor(AppContext.BaseDirectory);
    }

    /// <summary>The facade never throws: a spawn failure (bad DayzPath, missing exe) must come
    /// back to the CLI/MCP/tray as an OpResult, not an unhandled Win32Exception.</summary>
    private static OpResult Op(Func<string> action)
    {
        try { return new OpResult(true, action()); }
        catch (Exception ex) { return new OpResult(false, ex.Message); }
    }

    public OpResult Start(string mode, bool client, string source = "cli")
    {
        var (cfg, _, _) = Resolve();
        AutoLaunchTrayIfWanted(cfg, source);
        return Op(() =>
        {
            ProcessManager.Spawn(mode, "server", cfg, source, _configPath);
            if (client) ProcessManager.Spawn(mode, "client", cfg, source, _configPath);
            return $"started server{(client ? " + client" : "")} ({mode})";
        });
    }

    public OpResult Stop(bool client, string source = "cli")  // source unused by Stop; keep for symmetry
    {
        var (cfg, _, _) = Resolve();
        return Op(() =>
        {
            ProcessManager.Stop("server", cfg, _configPath);
            if (client) ProcessManager.Stop("client", cfg, _configPath);
            return $"stopped server{(client ? " + client" : "")}";
        });
    }

    public OpResult Restart(string mode, string source = "cli")
    {
        var (cfg, _, _) = Resolve();
        return Op(() =>
        {
            ProcessManager.Restart(mode, cfg, _configPath, source);
            return $"restarted server ({mode})";
        });
    }

    public OpResult StartTarget(string target, string mode, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        AutoLaunchTrayIfWanted(cfg, source);
        return Op(() =>
        {
            ProcessManager.Spawn(mode, target, cfg, source, _configPath);
            return $"started {target} ({mode})";
        });
    }

    public OpResult StopTarget(string target, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        return Op(() =>
        {
            ProcessManager.Stop(target, cfg, _configPath);
            return $"stopped {target}";
        });
    }

    public OpResult RestartTarget(string target, string mode, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        return Op(() =>
        {
            if (target == "server")
                ProcessManager.Restart(mode, cfg, _configPath, source);
            else
            {
                ProcessManager.Stop(target, cfg, _configPath);
                ProcessManager.Spawn(mode, target, cfg, source, _configPath);
            }
            return $"restarted {target} ({mode})";
        });
    }

    /// <summary>Which mpmissions folder the server will actually load (from the active instance's
    /// serverDZ.cfg template) and whether that's the instance's own mission or the install's.</summary>
    public MissionCheckResult CheckMission()
    {
        var (cfg, _, _) = Resolve();
        return MissionCheck.Evaluate(cfg);
    }

    /// <summary>Repoint the active instance's serverDZ.cfg template at its own mission (absolute path) so
    /// the server loads it instead of the install's.</summary>
    public OpResult FixMissionTemplate()
    {
        var (cfg, _, _) = Resolve();
        var mission = MissionLocator.Resolve(cfg)?.MissionDir;
        if (mission is null) return new OpResult(false, "no instance mission to point the template at");
        return Op(() =>
        {
            ServerScaffold.EnsureAbsoluteTemplate(cfg.ConfigName, mission);
            return "mission template now points at this instance";
        });
    }
}
