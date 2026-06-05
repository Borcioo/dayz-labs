using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Ipc;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Projects;
using Dzl.Core.Tools;

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
        var box = m.Enabled ? "[x]" : "[ ]";
        var tag = m.Side == "both" ? "" : $"  ({m.Side})";
        Console.WriteLine($"{box} {m.Path}{tag}");
    }
});

var modsProjectsCmd = new Command("projects", "List mod source projects under ProjectsRoot.");
modsProjectsCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var root2 = ProjectPaths.Root(cfg);
    var projects = ModProjects.Discover(root2);
    if (projects.Count == 0)
    {
        Console.WriteLine("(no mod projects)");
        return;
    }
    foreach (var p in projects)
    {
        var linked = p.Linked ? "linked" : "unlinked";
        Console.WriteLine($"{p.Name}  [{linked}]  {p.Path}");
    }
});
modsCmd.AddCommand(modsProjectsCmd);
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
    if (dryRun)
    {
        var targets = new List<string> { "server" };
        if (client) targets.Add("client");
        foreach (var target in targets)
        {
            var exe = target == "server"
                ? ProcessManager.ServerExe(cfg, mode)
                : ProcessManager.ClientExe(cfg, mode);
            var args = ArgvBuilder.Build(mode, target, cfg);
            Console.WriteLine($"{exe} {string.Join(' ', args)}");
        }
        return;
    }
    new ControlPlane(configPath).StartJson(mode, client, "cli");
    Console.WriteLine($"started server{(client ? " + client" : "")} ({mode})");
});
root.AddCommand(startCmd);

// ---- stop ----
var stopClient = new Option<bool>("--client", "Also stop the client.");
var stopCmd = new Command("stop", "Stop server (and client with --client).") { stopClient };
stopCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var client = ctx.ParseResult.GetValueForOption(stopClient);
    new ControlPlane(configPath).StopJson(client);
    Console.WriteLine($"stopped server{(client ? " + client" : "")}");
});
root.AddCommand(stopCmd);

// ---- restart ----
var restartDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
var restartNormal = new Option<bool>("--normal", "Normal (release) mode.");
var restartCmd = new Command("restart", "Restart the server.") { restartDebug, restartNormal };
restartCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var mode = ctx.ParseResult.GetValueForOption(restartNormal) ? "normal" : "debug";
    new ControlPlane(configPath).RestartJson(mode, "cli");
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
    var (_, _, _, configPath) = Resolve(ctx);
    var report = new LauncherService(configPath).Status();

    if (ctx.ParseResult.GetValueForOption(statusJson))
    {
        Console.WriteLine(JsonSerializer.Serialize(report, ConfigStore.Json));
        return;
    }

    string Line(TargetState t) => t.State == "down"
        ? "down"
        : $"up (pid {t.Pid}, {t.Mode}, src {t.Source})";

    Console.WriteLine($"mode:          {report.Mode}");
    Console.WriteLine($"port:          {report.Port}");
    Console.WriteLine($"active server: {report.ActivePreset ?? "(none)"}");
    Console.WriteLine($"server:        {Line(report.Server)}");
    Console.WriteLine($"client:        {Line(report.Client)}");
    Console.WriteLine($"dayz:          {report.Paths.GetValueOrDefault("dayz_path")}");
    Console.WriteLine($"profiles:      {report.Paths.GetValueOrDefault("profiles_path")}");
    Console.WriteLine($"client prof:   {report.Paths.GetValueOrDefault("client_profiles_path")}");
    Console.WriteLine($"config dir:    {report.Paths.GetValueOrDefault("config_dir")}");
    Console.WriteLine($"servers dir:   {report.Paths.GetValueOrDefault("presets_dir")}");
    Console.WriteLine($"projects root: {report.Paths.GetValueOrDefault("projects_root")}");
    Console.WriteLine($"enabled mods:  {report.Mods.Count}");
    foreach (var m in report.Mods)
        Console.WriteLine($"  - {m.Path}  ({m.Side})");
    Console.WriteLine("logs:");
    foreach (var kv in report.Logs)
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
    var (cfg, _, active, configPath) = Resolve(ctx);
    var folder = ctx.ParseResult.GetValueForArgument(addRootArg);
    var roots = new List<string>(cfg.ScanRoots);
    if (!roots.Contains(folder)) roots.Add(folder);
    cfg = cfg with { ScanRoots = roots };
    GlobalStore.Save(cfg.GlobalPart(active), configPath);   // scan-roots are global
    foreach (var r in cfg.ScanRoots) Console.WriteLine(r);
});
configCmd.AddCommand(addRootCmd);

var rmRootArg = new Argument<string>("folder", "Folder to stop scanning.");
var rmRootCmd = new Command("rm-root", "Remove a mod scan-root folder.") { rmRootArg };
rmRootCmd.SetHandler(ctx =>
{
    var (cfg, _, active, configPath) = Resolve(ctx);
    var folder = ctx.ParseResult.GetValueForArgument(rmRootArg);
    var roots = cfg.ScanRoots.Where(r => !string.Equals(r, folder, StringComparison.OrdinalIgnoreCase)).ToList();
    cfg = cfg with { ScanRoots = roots };
    GlobalStore.Save(cfg.GlobalPart(active), configPath);   // scan-roots are global
    foreach (var r in cfg.ScanRoots) Console.WriteLine(r);
});
configCmd.AddCommand(rmRootCmd);

var setKeyArg = new Argument<string>("key", "Editable scalar key.");
var setValArg = new Argument<string>("value", "New value.");
var setCmd = new Command("set", "Set a scalar key, e.g. dzl config set port 2402.") { setKeyArg, setValArg };
setCmd.SetHandler(ctx =>
{
    var (cfg, _, active, configPath) = Resolve(ctx);
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
        case "projects_root": updated = cfg with { ProjectsRoot = value }; break;
        default:
            Console.Error.WriteLine(
                "unknown/non-editable key '" + key + "'. editable: " +
                "port, player_name, connect_ip, mission, config_name, " +
                "dayz_path, profiles_path, client_profiles_path, projects_root");
            ctx.ExitCode = 1;
            return;
    }
    // Write both slices: globals to config.json, per-server to the active instance file.
    GlobalStore.Save(updated.GlobalPart(active), configPath);
    Profiles.Save(updated, string.IsNullOrEmpty(active) ? "default" : active, configPath);
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
        "projects_root" => updated.ProjectsRoot,
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
    var (_, _, _, configPath) = Resolve(ctx);
    var presets = new LauncherService(configPath).Presets();
    if (presets.Count == 0)
    {
        Console.WriteLine("(no presets)");
        return;
    }
    foreach (var p in presets)
        Console.WriteLine(p.Active ? $"* {p.Name}" : $"  {p.Name}");
});

var presetSaveArg = new Argument<string>("name", "Preset name.");
var presetSaveCmd = new Command("save", "Save the current config as a preset and activate it.") { presetSaveArg };
presetSaveCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(presetSaveArg);
    var res = new LauncherService(configPath).SaveActivePresetAs(name);
    Console.WriteLine(res.Message);
});
presetCmd.AddCommand(presetSaveCmd);

var presetLoadArg = new Argument<string>("name", "Preset name.");
var presetLoadCmd = new Command("load", "Make a preset active.") { presetLoadArg };
presetLoadCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(presetLoadArg);
    var res = new LauncherService(configPath).SetPreset(name);
    if (!res.Ok)
    {
        Console.Error.WriteLine(res.Message);
        ctx.ExitCode = 1;
        return;
    }
    Console.WriteLine(res.Message);
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

// ---- tools ----
string ToolsPath(InvocationContext ctx) => Resolve(ctx).cfg.DayzToolsPath;

void ListTools(InvocationContext ctx)
{
    foreach (var t in ToolCatalog.Discover(ToolsPath(ctx)))
    {
        var present = t.Exists ? "present" : "missing";
        var kind = t.Kind == ToolKind.CliWrappable ? "cli" : "launch";
        Console.WriteLine($"{t.Key,-16} {t.DisplayName,-22} [{present}]  ({kind})");
    }
}

var toolsCmd = new Command("tools", "Discover and launch DayZ Tools.");
toolsCmd.SetHandler(ListTools);

var toolsListCmd = new Command("list", "List discovered DayZ Tools.");
toolsListCmd.SetHandler(ListTools);
toolsCmd.AddCommand(toolsListCmd);

var toolsOpenArg = new Argument<string>("key", "Tool key (see 'tools list').");
var toolsOpenCmd = new Command("open", "Launch a tool by key.") { toolsOpenArg };
toolsOpenCmd.SetHandler(ctx =>
{
    var key = ctx.ParseResult.GetValueForArgument(toolsOpenArg);
    var tool = ToolCatalog.Find(ToolsPath(ctx), key);
    if (tool is null || !tool.Exists)
    {
        Console.Error.WriteLine($"tool not found or missing: {key}");
        ctx.ExitCode = 1;
        return;
    }
    if (!ToolLauncher.Launch(tool))
    {
        Console.Error.WriteLine($"failed to launch: {key}");
        ctx.ExitCode = 1;
        return;
    }
    Console.WriteLine($"launched {tool.DisplayName}");
});
toolsCmd.AddCommand(toolsOpenCmd);
root.AddCommand(toolsCmd);

// ---- paa ----
var paaDirArg = new Argument<string>("dir", "Folder of .png/.tga to convert to .paa.");
var paaRecursive = new Option<bool>("--recursive", "Recurse into subfolders.");
var paaCmd = new Command("paa", "Batch convert PNG/TGA to PAA (ImageToPAA).") { paaDirArg, paaRecursive };
paaCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var dir = ctx.ParseResult.GetValueForArgument(paaDirArg);
    var recursive = ctx.ParseResult.GetValueForOption(paaRecursive);
    var paaExe = ToolCatalog.Find(cfg.DayzToolsPath, "imagetopaa");
    if (paaExe is null || !paaExe.Exists)
    {
        Console.Error.WriteLine("tool not found: imagetopaa");
        ctx.ExitCode = 1;
        return;
    }
    foreach (var job in ImageToPaa.PlanFolder(dir, recursive).Where(j => !j.SuffixOk))
        Console.WriteLine($"warn: {Path.GetFileName(job.Input)} has no DayZ texture suffix");
    var results = ImageToPaa.ConvertFolder(paaExe.ExePath, dir, recursive,
        new Progress<PaaResult>(r =>
            Console.WriteLine($"{(r.Ok ? "ok " : "ERR")} {r.Input} -> {r.Output} {(r.Ok ? "" : r.Message)}")));
    var failed = results.Count(r => !r.Ok);
    Console.WriteLine($"{results.Count - failed} converted, {failed} failed");
    if (failed > 0) ctx.ExitCode = 1;
});
root.AddCommand(paaCmd);

// ---- pack ----
var packSrcArg = new Argument<string>("src", "Source folder to pack.");
var packDstArg = new Argument<string>("dst", "Output folder for the PBO.");
var packPrefix = new Option<string?>("--prefix", () => null, "PBO prefix.");
var packSign = new Option<string?>("--sign", () => null, "Private key file to sign with.");
var packNoClear = new Option<bool>("--no-clear", "Do not clear temp before packing.");
var packCmd = new Command("pack", "Pack a folder into a PBO (Addon Builder).")
{ packSrcArg, packDstArg, packPrefix, packSign, packNoClear };
packCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var src = ctx.ParseResult.GetValueForArgument(packSrcArg);
    var dst = ctx.ParseResult.GetValueForArgument(packDstArg);
    var prefix = ctx.ParseResult.GetValueForOption(packPrefix);
    var sign = ctx.ParseResult.GetValueForOption(packSign);
    var noClear = ctx.ParseResult.GetValueForOption(packNoClear);
    var addonExe = ToolCatalog.Find(cfg.DayzToolsPath, "addonbuilder");
    if (addonExe is null || !addonExe.Exists)
    {
        Console.Error.WriteLine("tool not found: addonbuilder");
        ctx.ExitCode = 1;
        return;
    }
    var res = AddonBuilder.Pack(addonExe.ExePath, src, dst, clear: !noClear, packOnly: true, prefix: prefix, signKey: sign);
    Console.WriteLine($"exit {res.ExitCode}");
    if (!string.IsNullOrWhiteSpace(res.Output)) Console.WriteLine(res.Output);
    if (!res.Ok) ctx.ExitCode = 1;
});
root.AddCommand(packCmd);

// ---- derap ----
var derapBinArg = new Argument<string>("bin", "config.bin to unbinarize.");
var derapOutArg = new Argument<string?>("out", () => null, "Output .cpp (defaults to same name).");
var derapCmd = new Command("derap", "Unbinarize a config.bin to .cpp (CfgConvert).") { derapBinArg, derapOutArg };
derapCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var bin = ctx.ParseResult.GetValueForArgument(derapBinArg);
    var outCpp = ctx.ParseResult.GetValueForArgument(derapOutArg) ?? Path.ChangeExtension(bin, ".cpp");
    var cfgExe = ToolCatalog.Find(cfg.DayzToolsPath, "cfgconvert");
    if (cfgExe is null || !cfgExe.Exists)
    {
        Console.Error.WriteLine("tool not found: cfgconvert");
        ctx.ExitCode = 1;
        return;
    }
    var (ok, output) = CfgConvert.Unbinarize(cfgExe.ExePath, bin, outCpp);
    Console.WriteLine(ok ? $"unbinarized -> {outCpp}" : "failed");
    if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
    if (!ok) ctx.ExitCode = 1;
});
root.AddCommand(derapCmd);

// ---- server ----
var serverCmd = new Command("server", "Manage server instances.");

var serverNewNameArg = new Argument<string>("name", "Instance name.");
var serverNewMap = new Option<string>("--map", () => "chernarus", "Map name (e.g. chernarus, livonia).");
var serverNewPort = new Option<int?>("--port", () => null, "UDP port (auto-assigned if omitted).");
var serverNewNoActivate = new Option<bool>("--no-activate", "Don't activate the new instance preset.");
var serverNewCmd = new Command("new", "Scaffold a new server instance.") { serverNewNameArg, serverNewMap, serverNewPort, serverNewNoActivate };
serverNewCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(serverNewNameArg);
    var map = ctx.ParseResult.GetValueForOption(serverNewMap)!;
    var port = ctx.ParseResult.GetValueForOption(serverNewPort);
    var noActivate = ctx.ParseResult.GetValueForOption(serverNewNoActivate);
    var r = new ServerService(configPath).Create(name, map, port, activate: !noActivate);
    if (!r.Ok)
    {
        Console.Error.WriteLine(r.Message);
        ctx.ExitCode = 1;
        return;
    }
    Console.WriteLine($"{r.Message}  (port {r.Port}, {r.Dir})");
});
serverCmd.AddCommand(serverNewCmd);

var serverLsCmd = new Command("ls", "List server instances.");
serverLsCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var list = new ServerService(configPath).List();
    if (list.Count == 0)
    {
        Console.WriteLine("(no servers)");
        return;
    }
    foreach (var i in list)
        Console.WriteLine($"{i.Name}  {i.Dir}");
});
serverCmd.AddCommand(serverLsCmd);

var serverUseNameArg = new Argument<string>("name", "Instance / preset name.");
var serverUseCmd = new Command("use", "Activate a server instance preset.") { serverUseNameArg };
serverUseCmd.SetHandler(ctx =>
{
    var (_, _, _, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(serverUseNameArg);
    var res = new LauncherService(configPath).SetPreset(name);
    if (!res.Ok)
    {
        Console.Error.WriteLine(res.Message);
        ctx.ExitCode = 1;
        return;
    }
    Console.WriteLine(res.Message);
});
serverCmd.AddCommand(serverUseCmd);

var serverRmNameArg = new Argument<string>("name", "Instance / preset name.");
var serverRmPurge = new Option<bool>("--purge", "Also delete the server's files (serverDZ.cfg, mpmissions, profiles).");
var serverRmCmd = new Command("rm", "Remove a server (keeps its files unless --purge).") { serverRmNameArg, serverRmPurge };
serverRmCmd.SetHandler(ctx =>
{
    var (_, _, active, configPath) = Resolve(ctx);
    var name = ctx.ParseResult.GetValueForArgument(serverRmNameArg);
    var purge = ctx.ParseResult.GetValueForOption(serverRmPurge);
    var serversDir = Path.Combine(ProjectPaths.Root(Profiles.ResolveActive(configPath).cfg), "servers", name);
    if (Profiles.Delete(name, configPath, purge))
    {
        if (active == name)
        {
            var remaining = Profiles.List(configPath);
            if (remaining.Count > 0) Profiles.SetActive(remaining[0], configPath);
            else { Profiles.SetActive("", configPath); Profiles.EnsureDefault(configPath); }
        }
        Console.WriteLine(purge
            ? $"deleted server '{name}' and its files"
            : $"removed server '{name}' (files kept on disk at {serversDir})");
    }
    else
    {
        Console.Error.WriteLine($"no preset '{name}'");
        ctx.ExitCode = 1;
    }
});
serverCmd.AddCommand(serverRmCmd);
root.AddCommand(serverCmd);

// ---- workdrive ----
var workdriveActionArg = new Argument<string>("action", "status|mount|unmount")
    .FromAmong("status", "mount", "unmount");
var workdriveCmd = new Command("workdrive", "Check/mount/unmount the P: work drive.") { workdriveActionArg };
workdriveCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var action = ctx.ParseResult.GetValueForArgument(workdriveActionArg);
    switch (action)
    {
        case "mount":
            var wdExe = Path.Combine(cfg.DayzToolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
            WorkDrive.Mount(File.Exists(wdExe) ? wdExe : "", EnvDetect.WorkDir(cfg.DayzToolsPath));
            Console.WriteLine(WorkDrive.IsMounted() ? "P: mounted" : "P: not mounted");
            break;
        case "unmount":
            var wdExeOff = Path.Combine(cfg.DayzToolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
            WorkDrive.Unmount(File.Exists(wdExeOff) ? wdExeOff : "");
            Console.WriteLine(WorkDrive.IsMounted() ? "P: mounted" : "P: not mounted");
            break;
        default:
            Console.WriteLine(WorkDrive.IsMounted() ? "P: mounted" : "P: not mounted");
            break;
    }
});
root.AddCommand(workdriveCmd);

// ---- new ----
var newModArg = new Argument<string>("Mod", "Mod name (letters, digits, underscores; start with letter).");
var newAuthorOpt = new Option<string?>("--author", () => null, "Author handle (cached for future use).");
var newCmd = new Command("new", "Scaffold a new DayZ mod source project.") { newModArg, newAuthorOpt };
newCmd.SetHandler(ctx =>
{
    var (cfg, _, _, configPath) = Resolve(ctx);
    var mod = ctx.ParseResult.GetValueForArgument(newModArg);
    var authorOpt = ctx.ParseResult.GetValueForOption(newAuthorOpt);
    var configDir = Path.GetDirectoryName(configPath)!;
    var author = authorOpt ?? ModScaffold.CachedAuthor(configDir);
    if (author is null)
    {
        Console.Error.WriteLine("no author; pass --author <handle> once to cache it");
        ctx.ExitCode = 1;
        return;
    }
    var projectsRoot = ProjectPaths.Root(cfg);
    var result = ModScaffold.Scaffold(projectsRoot, mod, author);
    Console.WriteLine(result.Message);
    if (authorOpt is not null) ModScaffold.SaveAuthor(configDir, authorOpt);
    if (result.Ok)
    {
        var linkResult = Junction.Ensure(ProjectPaths.WorkDriveLink(mod), ProjectPaths.ModDir(projectsRoot, mod));
        if (!linkResult.Ok)
            Console.WriteLine($"warning: link {linkResult.Action}: {linkResult.Detail}");
        else
            Console.WriteLine($"{linkResult.Action}: {linkResult.Detail}");
    }
    else
    {
        ctx.ExitCode = 1;
    }
});
root.AddCommand(newCmd);

// ---- import ----
var importPathArg = new Argument<string>("path", "Path to an existing mod source folder.");
var importNameOpt = new Option<string?>("--name", () => null, "Override the mod name (defaults to folder name).");
var importCmd = new Command("import", "Import an existing mod source folder into ProjectsRoot.") { importPathArg, importNameOpt };
importCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var path = ctx.ParseResult.GetValueForArgument(importPathArg);
    var name = ctx.ParseResult.GetValueForOption(importNameOpt);
    var result = ModImport.Import(ProjectPaths.Root(cfg), path, name);
    Console.WriteLine(result.Message);
    if (!result.Ok) ctx.ExitCode = 1;
});
root.AddCommand(importCmd);

// ---- link ----
var linkModArg = new Argument<string>("Mod", "Mod name to link on P:.");
var linkCmd = new Command("link", "Create or repair the P:\\ junction for a mod.") { linkModArg };
linkCmd.SetHandler(ctx =>
{
    var (cfg, _, _, _) = Resolve(ctx);
    var mod = ctx.ParseResult.GetValueForArgument(linkModArg);
    var projectsRoot = ProjectPaths.Root(cfg);
    var result = Junction.Ensure(ProjectPaths.WorkDriveLink(mod), ProjectPaths.ModDir(projectsRoot, mod));
    Console.WriteLine($"{result.Action}: {result.Detail}");
    if (!result.Ok) ctx.ExitCode = 1;
});
root.AddCommand(linkCmd);

return await root.InvokeAsync(args);
