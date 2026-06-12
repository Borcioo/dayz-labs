using System.CommandLine;
using Dzl.Core.App;
using Dzl.Core.Bases;
using Dzl.Core.Config;
using Dzl.Core.Projects;
using Dzl.Core.Servers;

namespace Dzl.Cli.Commands;

/// <summary>server (new / ls / use / rm) and base (ls / new / rm).</summary>
internal static class ServerCommands
{
    public static Command Server(CliContext c)
    {
        var serverCmd = new Command("server", "Manage server instances.");

        var serverNewNameArg = new Argument<string>("name", "Instance name.");
        var serverNewMap = new Option<string>("--map", () => "chernarus", "Map name (e.g. chernarus, livonia).");
        var serverNewPort = new Option<int?>("--port", () => null, "UDP port (auto-assigned if omitted).");
        var serverNewNoActivate = new Option<bool>("--no-activate", "Don't activate the new instance preset.");
        var serverNewBase = new Option<string?>("--base", () => null, "Create from a base/template instead of the DayZ install.");
        var serverNewCmd = new Command("new", "Scaffold a new server instance.") { serverNewNameArg, serverNewMap, serverNewPort, serverNewNoActivate, serverNewBase };
        serverNewCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var name = ctx.ParseResult.GetValueForArgument(serverNewNameArg);
            var map = ctx.ParseResult.GetValueForOption(serverNewMap)!;
            var port = ctx.ParseResult.GetValueForOption(serverNewPort);
            var noActivate = ctx.ParseResult.GetValueForOption(serverNewNoActivate);
            var baseName = ctx.ParseResult.GetValueForOption(serverNewBase);
            var r = new ServerService(configPath).Create(name, map, port, activate: !noActivate, baseName: baseName);
            if (!r.Ok)
            {
                CliOut.Fail(ctx, r.Message);
                return;
            }
            Console.WriteLine($"{r.Message}  (port {r.Port}, {r.Dir})");
        });
        serverCmd.AddCommand(serverNewCmd);

        var serverLsCmd = new Command("ls", "List server instances.");
        serverLsCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var list = new ServerService(configPath).List();
            CliOut.List(list, "(no servers)", i => $"{i.Name}  {i.Dir}");
        });
        serverCmd.AddCommand(serverLsCmd);

        var serverUseNameArg = new Argument<string>("name", "Instance / preset name.");
        var serverUseCmd = new Command("use", "Activate a server instance preset.") { serverUseNameArg };
        // Same operation as 'dzl preset load' — shared handler body.
        serverUseCmd.SetHandler(ctx =>
            PresetCommands.Activate(c, ctx, ctx.ParseResult.GetValueForArgument(serverUseNameArg)));
        serverCmd.AddCommand(serverUseCmd);

        var serverRmNameArg = new Argument<string>("name", "Instance / preset name.");
        var serverRmPurge = new Option<bool>("--purge", "Also delete the server's files (serverDZ.cfg, mpmissions, profiles).");
        var serverRmCmd = new Command("rm", "Remove a server (keeps its files unless --purge).") { serverRmNameArg, serverRmPurge };
        serverRmCmd.SetHandler(ctx =>
        {
            var (_, _, active, configPath) = c.Resolve(ctx);
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
                CliOut.Fail(ctx, $"no preset '{name}'");
            }
        });
        serverCmd.AddCommand(serverRmCmd);
        return serverCmd;
    }

    public static Command Base(CliContext c)
    {
        var baseCmd = new Command("base", "Manage server bases (templates) new instances can be created from.");

        var baseLsCmd = new Command("ls", "List bases.");
        baseLsCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var bases = ServerBases.List(ProjectPaths.Root(cfg));
            CliOut.List(bases, "(no bases)", b =>
                $"{b.Name,-20} {b.Source,-13} {(string.IsNullOrEmpty(b.Mission) ? "" : b.Mission + "  ")}{(string.IsNullOrEmpty(b.DayzVersion) ? "" : "DayZ " + b.DayzVersion)}");
        });
        baseCmd.AddCommand(baseLsCmd);

        var baseNewNameArg = new Argument<string>("name", "Base name.");
        var baseNewEmpty = new Option<bool>("--empty", "Create an empty/custom base (you add the files), instead of copying the DayZ install.");
        var baseNewMap = new Option<string>("--map", () => "chernarus", "Map to snapshot from the install (when not --empty).");
        var baseNewCmd = new Command("new", "Create a base — from the DayZ install (default) or empty (--empty).")
        { baseNewNameArg, baseNewEmpty, baseNewMap };
        baseNewCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var root = ProjectPaths.Root(cfg);
            var name = ctx.ParseResult.GetValueForArgument(baseNewNameArg);
            var (ok, message) = ctx.ParseResult.GetValueForOption(baseNewEmpty)
                ? ServerBases.CreateEmpty(root, name)
                : ServerBases.CreateFromInstall(root, name, cfg.DayzPath, MapAliases.MissionTemplate(ctx.ParseResult.GetValueForOption(baseNewMap)!));
            CliOut.Result(ctx, ok, message);
        });
        baseCmd.AddCommand(baseNewCmd);

        var baseRmNameArg = new Argument<string>("name", "Base name.");
        var baseRmCmd = new Command("rm", "Delete a base.") { baseRmNameArg };
        baseRmCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var name = ctx.ParseResult.GetValueForArgument(baseRmNameArg);
            if (ServerBases.Delete(ProjectPaths.Root(cfg), name)) Console.WriteLine($"deleted base '{name}'");
            else CliOut.Fail(ctx, $"no base '{name}'");
        });
        baseCmd.AddCommand(baseRmCmd);
        return baseCmd;
    }
}
