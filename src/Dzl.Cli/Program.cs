using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Dzl.Core.Config;
using Dzl.Core.Launch;
using Dzl.Core.Logs;

// Global --config option (shared instance read inside handlers).
var defaultConfig = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "dzl", "config.json");
var configOption = new Option<string?>("--config", () => null, "Path to config.json");

// Resolve config path + ensure default preset, then resolve the active preset.
// Returns (cfg, savePath, active, configPath).
(DzlConfig cfg, string savePath, string active, string configPath) Resolve(InvocationContext ctx)
{
    var raw = ctx.ParseResult.GetValueForOption(configOption);
    var configPath = string.IsNullOrWhiteSpace(raw) ? defaultConfig : raw;
    Profiles.EnsureDefault(configPath);
    var (cfg, savePath, active) = Profiles.ResolveActive(configPath);
    return (cfg, savePath, active, configPath);
}

var root = new RootCommand("dzl - DayZ dev launcher");
root.AddGlobalOption(configOption);

// ---- mods ----
var modsCmd = new Command("mods", "List the current ordered mod selection.");
modsCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    foreach (var m in cfg.Mods)
    {
        var mark = m.Enabled ? "[x]" : "[ ]";
        var tag = m.Side == "both" ? "" : $"  ({m.Side})";
        Console.WriteLine($"{mark} {m.Path}{tag}");
    }
});
root.AddCommand(modsCmd);

// ---- start ----
var startDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
var startNormal = new Option<bool>("--normal", "Normal (release) mode.");
var startClient = new Option<bool>("--client", "Also start the client.");
var startDryRun = new Option<bool>("--dry-run", "Print argv, don't spawn.");
var startCmd = new Command("start", "Start the server (and optionally client).")
{ startDebug, startNormal, startClient, startDryRun };
startCmd.SetHandler(ctx =>
{
    var (cfg, _, _, configPath) = Resolve(ctx);
    var normal = ctx.ParseResult.GetValueForOption(startNormal);
    var mode = normal ? "normal" : "debug";
    var client = ctx.ParseResult.GetValueForOption(startClient);
    var dryRun = ctx.ParseResult.GetValueForOption(startDryRun);
    var targets = new List<string> { "server" };
    if (client) targets.Add("client");
    foreach (var target in targets)
    {
        if (dryRun)
        {
            var exe = target == "server"
                ? ProcessManager.ServerExe(cfg, mode)
                : ProcessManager.ClientExe(cfg, mode);
            var args = ArgvBuilder.Build(mode, target, cfg);
            Console.WriteLine($"{exe} {string.Join(' ', args)}");
        }
        else
        {
            ProcessManager.Spawn(mode, target, cfg, "cli", configPath);
            Console.WriteLine($"started {target} ({mode})");
        }
    }
});
root.AddCommand(startCmd);

// ---- stop ----
var stopClient = new Option<bool>("--client", "Also stop the client.");
var stopCmd = new Command("stop", "Stop server (and client with --client).") { stopClient };
stopCmd.SetHandler(ctx =>
{
    var (cfg, _, _, configPath) = Resolve(ctx);
    ProcessManager.Stop("server", cfg, configPath);
    Console.WriteLine("stopped server");
    if (ctx.ParseResult.GetValueForOption(stopClient))
    {
        ProcessManager.Stop("client", cfg, configPath);
        Console.WriteLine("stopped client");
    }
});
root.AddCommand(stopCmd);

// ---- restart ----
var restartDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
var restartNormal = new Option<bool>("--normal", "Normal (release) mode.");
var restartCmd = new Command("restart", "Restart the server.") { restartDebug, restartNormal };
restartCmd.SetHandler(ctx =>
{
    var (cfg, _, _, configPath) = Resolve(ctx);
    var mode = ctx.ParseResult.GetValueForOption(restartNormal) ? "normal" : "debug";
    ProcessManager.Restart(mode, cfg, configPath, "cli");
    Console.WriteLine("restarted server");
});
root.AddCommand(restartCmd);

// ---- logs ----
var logsWhich = new Argument<string>("which", "script|rpt|adm|client")
    .FromAmong("script", "rpt", "adm", "client");
var logsLines = new Option<int?>("--lines", "Print the last N lines and exit.");
var logsCmd = new Command("logs", "Resolve a log path (or print last N lines with --lines).")
{ logsWhich, logsLines };
logsCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var which = ctx.ParseResult.GetValueForArgument(logsWhich);
    var lines = ctx.ParseResult.GetValueForOption(logsLines);
    var path = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath).GetValueOrDefault(which);
    if (string.IsNullOrEmpty(path))
    {
        Console.WriteLine($"no {which} log found");
        return;
    }
    if (lines is int n)
    {
        foreach (var line in LogTail.LastLines(path, n)) Console.WriteLine(line);
        return;
    }
    Console.WriteLine(path);
});
root.AddCommand(logsCmd);

// ---- status ----
var statusJson = new Option<bool>("--json", "Print machine-readable JSON.");
var statusCmd = new Command("status", "Show launcher status.") { statusJson };
statusCmd.SetHandler(ctx =>
{
    var (cfg, _, active, configPath) = Resolve(ctx);
    var live = StateFile.ReadLive(configPath, ProcessManager.ImageOf);

    object StateFor(string target)
    {
        if (live.TryGetValue(target, out var info))
            return new { state = "up", source = info.Source, mode = info.Mode, pid = info.Pid };
        return new { state = "down", source = (string?)null, mode = (string?)null, pid = (int?)null };
    }

    var logs = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath);
    var enabledMods = cfg.Mods.Where(m => m.Enabled)
        .Select(m => new { path = m.Path, side = m.Side }).ToList();

    var status = new
    {
        mode = cfg.Mode,
        port = cfg.Port,
        active_preset = string.IsNullOrEmpty(active) ? null : active,
        server = StateFor("server"),
        client = StateFor("client"),
        paths = new
        {
            dayz_path = cfg.DayzPath,
            profiles_path = cfg.ProfilesPath,
            client_profiles_path = cfg.ClientProfilesPath,
            config_dir = Path.GetDirectoryName(configPath),
            presets_dir = Profiles.PresetsDir(configPath),
        },
        mods = enabledMods,
        logs,
    };

    if (ctx.ParseResult.GetValueForOption(statusJson))
    {
        Console.WriteLine(JsonSerializer.Serialize(status, ConfigStore.Json));
        return;
    }

    Console.WriteLine($"mode:          {status.mode}");
    Console.WriteLine($"port:          {status.port}");
    Console.WriteLine($"active preset: {status.active_preset ?? "(none)"}");
    var s = live.GetValueOrDefault("server");
    var c = live.GetValueOrDefault("client");
    Console.WriteLine($"server:        {(s is null ? "down" : $"up (pid {s.Pid}, {s.Mode}, src {s.Source})")}");
    Console.WriteLine($"client:        {(c is null ? "down" : $"up (pid {c.Pid}, {c.Mode}, src {c.Source})")}");
    Console.WriteLine($"dayz:          {cfg.DayzPath}");
    Console.WriteLine($"profiles:      {cfg.ProfilesPath}");
    Console.WriteLine($"client prof:   {cfg.ClientProfilesPath}");
    Console.WriteLine($"config dir:    {status.paths.config_dir}");
    Console.WriteLine($"presets dir:   {status.paths.presets_dir}");
    Console.WriteLine($"enabled mods:  {enabledMods.Count}");
    foreach (var m in enabledMods)
        Console.WriteLine($"  - {m.path}  ({m.side})");
    Console.WriteLine("logs:");
    foreach (var kv in logs)
        Console.WriteLine($"  {kv.Key,-7} {kv.Value ?? "(none)"}");
});
root.AddCommand(statusCmd);

// ---- config ----
var configCmd = new Command("config", "View/edit launcher config.");
configCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    Console.WriteLine(JsonSerializer.Serialize(cfg, ConfigStore.Json));
});

var configPathCmd = new Command("path", "Print the config.json location.");
configPathCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    Console.WriteLine(configPath);
});
configCmd.AddCommand(configPathCmd);

var addRootArg = new Argument<string>("folder", "Folder to scan for mods.");
var addRootCmd = new Command("add-root", "Add a folder to scan for mods.") { addRootArg };
addRootCmd.SetHandler(ctx =>
{
    var (cfg, savePath, _, _) = Resolve(ctx);
    var folder = ctx.ParseResult.GetValueForArgument(addRootArg);
    var roots = new List<string>(cfg.ScanRoots);
    if (!roots.Contains(folder)) roots.Add(folder);
    cfg = cfg with { ScanRoots = roots };
    ConfigStore.Save(cfg, savePath);
    foreach (var r in cfg.ScanRoots) Console.WriteLine(r);
});
configCmd.AddCommand(addRootCmd);

var rmRootArg = new Argument<string>("folder", "Folder to stop scanning.");
var rmRootCmd = new Command("rm-root", "Remove a mod scan-root folder.") { rmRootArg };
rmRootCmd.SetHandler(ctx =>
{
    var (cfg, savePath, _, _) = Resolve(ctx);
    var folder = ctx.ParseResult.GetValueForArgument(rmRootArg);
    var roots = cfg.ScanRoots.Where(r => !string.Equals(r, folder, StringComparison.OrdinalIgnoreCase)).ToList();
    cfg = cfg with { ScanRoots = roots };
    ConfigStore.Save(cfg, savePath);
    foreach (var r in cfg.ScanRoots) Console.WriteLine(r);
});
configCmd.AddCommand(rmRootCmd);

var setKeyArg = new Argument<string>("key", "Editable scalar key.");
var setValArg = new Argument<string>("value", "New value.");
var setCmd = new Command("set", "Set a scalar key, e.g. dzl config set port 2402.") { setKeyArg, setValArg };
setCmd.SetHandler(ctx =>
{
    var (cfg, savePath, _, _) = Resolve(ctx);
    var key = ctx.ParseResult.GetValueForArgument(setKeyArg);
    var value = ctx.ParseResult.GetValueForArgument(setValArg);
    DzlConfig updated;
    switch (key)
    {
        case "port":
            if (!int.TryParse(value, out var port))
            {
                Console.Error.WriteLine($"'{key}' must be a number");
                ctx.ExitCode = 1;
                return;
            }
            updated = cfg with { Port = port };
            break;
        case "player_name": updated = cfg with { PlayerName = value }; break;
        case "connect_ip": updated = cfg with { ConnectIp = value }; break;
        case "mission": updated = cfg with { Mission = value }; break;
        case "config_name": updated = cfg with { ConfigName = value }; break;
        case "dayz_path": updated = cfg with { DayzPath = value }; break;
        case "profiles_path": updated = cfg with { ProfilesPath = value }; break;
        case "client_profiles_path": updated = cfg with { ClientProfilesPath = value }; break;
        default:
            Console.Error.WriteLine(
                "unknown/non-editable key '" + key + "'. editable: " +
                "port, player_name, connect_ip, mission, config_name, " +
                "dayz_path, profiles_path, client_profiles_path");
            ctx.ExitCode = 1;
            return;
    }
    ConfigStore.Save(updated, savePath);
    var shown = key switch
    {
        "port" => updated.Port.ToString(),
        "player_name" => updated.PlayerName,
        "connect_ip" => updated.ConnectIp,
        "mission" => updated.Mission,
        "config_name" => updated.ConfigName,
        "dayz_path" => updated.DayzPath,
        "profiles_path" => updated.ProfilesPath,
        "client_profiles_path" => updated.ClientProfilesPath,
        _ => value,
    };
    Console.WriteLine($"{key} = {shown}");
});
configCmd.AddCommand(setCmd);
root.AddCommand(configCmd);

// ---- preset ----
var presetCmd = new Command("preset", "Save/load named config presets.");
presetCmd.SetHandler(ctx =>
{
    var (_, _, active, configPath) = Resolve(ctx);
    var presets = Profiles.List(configPath);
    if (presets.Count == 0)
    {
        Console.WriteLine("(no presets)");
        return;
    }
    foreach (var n in presets)
        Console.WriteLine(n == active ? $"* {n}" : $"  {n}");
});

var presetSaveArg = new Argument<string>("name", "Preset name.");
var presetSaveCmd = new Command("save", "Save the current config as a preset and activate it.") { presetSaveArg };
presetSaveCmd.SetHandler(ctx =>
{
    var (cfg, _, _, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(presetSaveArg);
    Profiles.Save(cfg, name, configPath);
    Profiles.SetActive(name, configPath);
    Console.WriteLine($"saved preset '{name}' (now active)");
});
presetCmd.AddCommand(presetSaveCmd);

var presetLoadArg = new Argument<string>("name", "Preset name.");
var presetLoadCmd = new Command("load", "Make a preset active.") { presetLoadArg };
presetLoadCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(presetLoadArg);
    var presets = Profiles.List(configPath);
    if (!presets.Contains(name))
    {
        Console.Error.WriteLine(
            $"no preset '{name}'. have: " + (presets.Count > 0 ? string.Join(", ", presets) : "(none)"));
        ctx.ExitCode = 1;
        return;
    }
    Profiles.SetActive(name, configPath);
    Console.WriteLine($"active preset -> '{name}'");
});
presetCmd.AddCommand(presetLoadCmd);

var presetRmArg = new Argument<string>("name", "Preset name.");
var presetRmCmd = new Command("rm", "Delete a preset.") { presetRmArg };
presetRmCmd.SetHandler(ctx =>
{
    var (_, _, active, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(presetRmArg);
    if (Profiles.Delete(name, configPath))
    {
        if (active == name) Profiles.SetActive("", configPath);
        Console.WriteLine($"deleted preset '{name}'");
    }
    else
    {
        Console.Error.WriteLine($"no preset '{name}'");
        ctx.ExitCode = 1;
    }
});
presetCmd.AddCommand(presetRmCmd);
root.AddCommand(presetCmd);

return await root.InvokeAsync(args);
