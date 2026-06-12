using System.CommandLine;
using System.Text.Json;
using Dzl.Core.Config;

namespace Dzl.Cli.Commands;

/// <summary>config (path / add-root / rm-root / set).</summary>
internal static class ConfigCommands
{
    public static Command Config(CliContext c)
    {
        var configCmd = new Command("config", "View/edit launcher config.");
        configCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            Console.WriteLine(JsonSerializer.Serialize(cfg, ConfigStore.Json));
        });

        var configPathCmd = new Command("path", "Print the config.json location.");
        configPathCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            Console.WriteLine(configPath);
        });
        configCmd.AddCommand(configPathCmd);

        var addRootArg = new Argument<string>("folder", "Folder to scan for mods.");
        var addRootCmd = new Command("add-root", "Add a folder to scan for mods.") { addRootArg };
        addRootCmd.SetHandler(ctx =>
        {
            var (cfg, _, active, configPath) = c.Resolve(ctx);
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
            var (cfg, _, active, configPath) = c.Resolve(ctx);
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
            var (cfg, _, active, configPath) = c.Resolve(ctx);
            var key = ctx.ParseResult.GetValueForArgument(setKeyArg);
            var value = ctx.ParseResult.GetValueForArgument(setValArg);
            DzlConfig updated;
            switch (key)
            {
                case "port":
                    if (!int.TryParse(value, out var port))
                    {
                        CliOut.Fail(ctx, $"'{key}' must be a number");
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
                    CliOut.Fail(ctx,
                        "unknown/non-editable key '" + key + "'. editable: " +
                        "port, player_name, connect_ip, mission, config_name, " +
                        "dayz_path, profiles_path, client_profiles_path, projects_root");
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
        return configCmd;
    }
}
