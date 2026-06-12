using System.CommandLine;
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Cli.Commands;

/// <summary>build, preflight, key (new).</summary>
internal static class BuildCommands
{
    public static Command Build(CliContext c)
    {
        var buildModArg = new Argument<string>("Mod", "Mod project name (under ProjectsRoot).");
        var buildClean = new Option<bool>("--clean", "Wipe the output first (AddonBuilder -clear).");
        var buildNoBin = new Option<bool>("--no-binarize", "Pack only, don't binarize (AddonBuilder -packonly).");
        var buildSign = new Option<bool>("--sign", "Sign the PBO with your signing key (generate one with 'dzl key new').");
        var buildForce = new Option<bool>("--force", "Rebuild even when nothing changed (ignore the skip-unchanged cache).");
        var buildKey = new Option<string?>("--key", "Sign with this key from the keys folder (default: the configured key).");
        var buildCmd = new Command("build", "Build a mod into a PBO and add it to the active server's run-list.")
            { buildModArg, buildClean, buildNoBin, buildSign, buildForce, buildKey };
        buildCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(buildModArg);
            var clean = ctx.ParseResult.GetValueForOption(buildClean);
            var noBin = ctx.ParseResult.GetValueForOption(buildNoBin);
            var sign = ctx.ParseResult.GetValueForOption(buildSign);
            var force = ctx.ParseResult.GetValueForOption(buildForce);
            var key = ctx.ParseResult.GetValueForOption(buildKey);
            var r = new BuildService(configPath).Build(mod, clean: clean, binarize: !noBin, sign: sign,
                onLine: line => Console.Error.WriteLine(line), force: force, keyName: key);   // log to stderr; result line to stdout
            if (!r.Ok)
            {
                CliOut.Fail(ctx, r.Message);
                if (r.Diagnostics.Length > 0) Console.Error.WriteLine(r.Diagnostics);
                return;
            }
            Console.WriteLine($"{r.Message}  →  {r.PboPath}");
        });
        return buildCmd;
    }

    public static Command Preflight(CliContext c)
    {
        var preflightModArg = new Argument<string>("Mod", "Mod project name (under ProjectsRoot).");
        var preflightJson = new Option<bool>("--json", "Print the full report as JSON instead of text.");
        var preflightCmd = new Command("preflight",
            "Validate a mod before building: configs (CfgPatches/CfgMods/syntax), references, paths, scripts.")
            { preflightModArg, preflightJson };
        preflightCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(preflightModArg);
            var asJson = ctx.ParseResult.GetValueForOption(preflightJson);
            var r = new BuildService(configPath).Preflight(mod);

            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(r, ConfigStore.Json));
            }
            else
            {
                foreach (var f in r.Findings.OrderByDescending(f => f.Severity))
                {
                    var mark = f.Severity switch
                    {
                        Dzl.Core.Build.Preflight.FindingSeverity.Error => "✗",
                        Dzl.Core.Build.Preflight.FindingSeverity.Warning => "!",
                        _ => "·",
                    };
                    var loc = f.File.Length > 0 ? (f.Line > 0 ? $"  [{f.File}:{f.Line}]" : $"  [{f.File}]") : "";
                    Console.WriteLine($"{mark} {f.Rule}: {f.Message}{loc}");
                }
                Console.WriteLine();
                Console.WriteLine($"{(r.Ok ? "✓" : "✗")} {mod}: {r.Errors} error(s), {r.Warnings} warning(s), {r.Infos} info");
                if (r.ReportTxt.Length > 0) Console.WriteLine($"report: {r.ReportTxt}");
            }
            if (!r.Ok) ctx.ExitCode = 1;
        });
        return preflightCmd;
    }

    public static Command Key(CliContext c)
    {
        var keyCmd = new Command("key", "Manage your DayZ signing key (one key signs all your mods).");
        var keyNewName = new Argument<string?>("name", () => null, "Key name (defaults to your configured signing key / author).");
        var keyNewCmd = new Command("new", "Create a signing key pair (DSCreateKey) in the keys folder.") { keyNewName };
        keyNewCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var name = ctx.ParseResult.GetValueForArgument(keyNewName);
            var r = new BuildService(configPath).GenerateKey(name);
            CliOut.Result(ctx, r.Ok, r.Ok ? $"✓ key: {r.PrivateKey}  (public {r.PublicKey})" : $"✗ {r.Output}");
        });
        keyCmd.AddCommand(keyNewCmd);
        return keyCmd;
    }
}
