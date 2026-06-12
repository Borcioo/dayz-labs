using System.CommandLine;
using System.CommandLine.Invocation;
using Dzl.Core.Env;
using Dzl.Core.Tools;

namespace Dzl.Cli.Commands;

/// <summary>tools (list / open), paa, pack, derap, workdrive.</summary>
internal static class ToolsCommands
{
    private static string ToolsPath(CliContext c, InvocationContext ctx) => c.Resolve(ctx).cfg.DayzToolsPath;

    private static void ListTools(CliContext c, InvocationContext ctx)
    {
        foreach (var t in ToolCatalog.Discover(ToolsPath(c, ctx)))
        {
            var present = t.Exists ? "present" : "missing";
            var kind = t.Kind == ToolKind.CliWrappable ? "cli" : "launch";
            Console.WriteLine($"{t.Key,-16} {t.DisplayName,-22} [{present}]  ({kind})");
        }
    }

    public static Command Tools(CliContext c)
    {
        var toolsCmd = new Command("tools", "Discover and launch DayZ Tools.");
        toolsCmd.SetHandler(ctx => ListTools(c, ctx));

        var toolsListCmd = new Command("list", "List discovered DayZ Tools.");
        toolsListCmd.SetHandler(ctx => ListTools(c, ctx));
        toolsCmd.AddCommand(toolsListCmd);

        var toolsOpenArg = new Argument<string>("key", "Tool key (see 'tools list').");
        var toolsOpenCmd = new Command("open", "Launch a tool by key.") { toolsOpenArg };
        toolsOpenCmd.SetHandler(ctx =>
        {
            var key = ctx.ParseResult.GetValueForArgument(toolsOpenArg);
            var tool = CliOut.RequireTool(ctx, ToolsPath(c, ctx), key);
            if (tool is null) return;
            if (!ToolLauncher.Launch(tool))
            {
                CliOut.Fail(ctx, $"failed to launch: {key}");
                return;
            }
            Console.WriteLine($"launched {tool.DisplayName}");
        });
        toolsCmd.AddCommand(toolsOpenCmd);
        return toolsCmd;
    }

    public static Command Paa(CliContext c)
    {
        var paaDirArg = new Argument<string>("dir", "Folder of .png/.tga to convert to .paa.");
        var paaRecursive = new Option<bool>("--recursive", "Recurse into subfolders.");
        var paaCmd = new Command("paa", "Batch convert PNG/TGA to PAA (ImageToPAA).") { paaDirArg, paaRecursive };
        paaCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var dir = ctx.ParseResult.GetValueForArgument(paaDirArg);
            var recursive = ctx.ParseResult.GetValueForOption(paaRecursive);
            var paaExe = CliOut.RequireTool(ctx, cfg.DayzToolsPath, "imagetopaa");
            if (paaExe is null) return;
            foreach (var job in ImageToPaa.PlanFolder(dir, recursive).Where(j => !j.SuffixOk))
                Console.WriteLine($"warn: {Path.GetFileName(job.Input)} has no DayZ texture suffix");
            var results = ImageToPaa.ConvertFolder(paaExe.ExePath, dir, recursive,
                new Progress<PaaResult>(r =>
                    Console.WriteLine($"{(r.Ok ? "ok " : "ERR")} {r.Input} -> {r.Output} {(r.Ok ? "" : r.Message)}")));
            var failed = results.Count(r => !r.Ok);
            Console.WriteLine($"{results.Count - failed} converted, {failed} failed");
            if (failed > 0) ctx.ExitCode = 1;
        });
        return paaCmd;
    }

    public static Command Pack(CliContext c)
    {
        var packSrcArg = new Argument<string>("src", "Source folder to pack.");
        var packDstArg = new Argument<string>("dst", "Output folder for the PBO.");
        var packPrefix = new Option<string?>("--prefix", () => null, "PBO prefix.");
        var packSign = new Option<string?>("--sign", () => null, "Private key file to sign with.");
        var packNoClear = new Option<bool>("--no-clear", "Do not clear temp before packing.");
        var packCmd = new Command("pack", "Pack a folder into a PBO (Addon Builder).")
        { packSrcArg, packDstArg, packPrefix, packSign, packNoClear };
        packCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var src = ctx.ParseResult.GetValueForArgument(packSrcArg);
            var dst = ctx.ParseResult.GetValueForArgument(packDstArg);
            var prefix = ctx.ParseResult.GetValueForOption(packPrefix);
            var sign = ctx.ParseResult.GetValueForOption(packSign);
            var noClear = ctx.ParseResult.GetValueForOption(packNoClear);
            var addonExe = CliOut.RequireTool(ctx, cfg.DayzToolsPath, "addonbuilder");
            if (addonExe is null) return;
            var res = AddonBuilder.Pack(addonExe.ExePath, src, dst, clear: !noClear, packOnly: true, prefix: prefix, signKey: sign);
            Console.WriteLine($"exit {res.ExitCode}");
            if (!string.IsNullOrWhiteSpace(res.Output)) Console.WriteLine(res.Output);
            if (!res.Ok) ctx.ExitCode = 1;
        });
        return packCmd;
    }

    public static Command Derap(CliContext c)
    {
        var derapBinArg = new Argument<string>("bin", "config.bin to unbinarize.");
        var derapOutArg = new Argument<string?>("out", () => null, "Output .cpp (defaults to same name).");
        var derapCmd = new Command("derap", "Unbinarize a config.bin to .cpp (CfgConvert).") { derapBinArg, derapOutArg };
        derapCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var bin = ctx.ParseResult.GetValueForArgument(derapBinArg);
            var outCpp = ctx.ParseResult.GetValueForArgument(derapOutArg) ?? Path.ChangeExtension(bin, ".cpp");
            var cfgExe = CliOut.RequireTool(ctx, cfg.DayzToolsPath, "cfgconvert");
            if (cfgExe is null) return;
            var (ok, output) = CfgConvert.Unbinarize(cfgExe.ExePath, bin, outCpp);
            Console.WriteLine(ok ? $"unbinarized -> {outCpp}" : "failed");
            if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
            if (!ok) ctx.ExitCode = 1;
        });
        return derapCmd;
    }

    public static Command Workdrive(CliContext c)
    {
        var workdriveActionArg = new Argument<string>("action", "status|mount|unmount")
            .FromAmong("status", "mount", "unmount");
        var workdriveCmd = new Command("workdrive", "Check/mount/unmount the P: work drive.") { workdriveActionArg };
        workdriveCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
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
        return workdriveCmd;
    }
}
