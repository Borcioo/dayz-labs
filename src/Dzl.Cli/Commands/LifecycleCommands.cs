using System.CommandLine;
using Dzl.Core.Ipc;
using Dzl.Core.Launch;

namespace Dzl.Cli.Commands;

/// <summary>start / stop / restart.</summary>
internal static class LifecycleCommands
{
    public static Command Start(CliContext c)
    {
        // Intentional no-op: '--debug' is the default mode, so the flag is accepted
        // (scripts may pass it) but never read — '--normal' is the only mode switch.
        var startDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
        var startNormal = new Option<bool>("--normal", "Normal (release) mode.");
        var startClient = new Option<bool>("--client", "Also start the client.");
        var startDryRun = new Option<bool>("--dry-run", "Print argv, don't spawn.");
        var startCmd = new Command("start", "Start the server (and optionally client).")
        { startDebug, startNormal, startClient, startDryRun };
        startCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, configPath) = c.Resolve(ctx);
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
        return startCmd;
    }

    public static Command Stop(CliContext c)
    {
        var stopClient = new Option<bool>("--client", "Also stop the client.");
        var stopCmd = new Command("stop", "Stop server (and client with --client).") { stopClient };
        stopCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var client = ctx.ParseResult.GetValueForOption(stopClient);
            new ControlPlane(configPath).StopJson(client);
            Console.WriteLine($"stopped server{(client ? " + client" : "")}");
        });
        return stopCmd;
    }

    public static Command Restart(CliContext c)
    {
        // Intentional no-op: '--debug' is the default mode, so the flag is accepted
        // (scripts may pass it) but never read — '--normal' is the only mode switch.
        var restartDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
        var restartNormal = new Option<bool>("--normal", "Normal (release) mode.");
        var restartCmd = new Command("restart", "Restart the server.") { restartDebug, restartNormal };
        restartCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mode = ctx.ParseResult.GetValueForOption(restartNormal) ? "normal" : "debug";
            new ControlPlane(configPath).RestartJson(mode, "cli");
            Console.WriteLine("restarted server");
        });
        return restartCmd;
    }
}
