using System.CommandLine;
using System.CommandLine.Invocation;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Cli.Commands;

/// <summary>preset (save / load / rm).</summary>
internal static class PresetCommands
{
    /// <summary>
    /// Shared handler body for activating a preset — used by both
    /// 'dzl preset load' and 'dzl server use' (they are the same operation).
    /// </summary>
    internal static void Activate(CliContext c, InvocationContext ctx, string name)
    {
        var (_, _, _, configPath) = c.Resolve(ctx);
        var res = new LauncherService(configPath).SetPreset(name);
        if (!res.Ok)
        {
            CliOut.Fail(ctx, res.Message);
            return;
        }
        Console.WriteLine(res.Message);
    }

    public static Command Preset(CliContext c)
    {
        var presetCmd = new Command("preset", "Save/load named config presets.");
        presetCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var presets = new LauncherService(configPath).Presets();
            CliOut.List(presets, "(no presets)", p => p.Active ? $"* {p.Name}" : $"  {p.Name}");
        });

        var presetSaveArg = new Argument<string>("name", "Preset name.");
        var presetSaveCmd = new Command("save", "Save the current config as a preset and activate it.") { presetSaveArg };
        presetSaveCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var name = ctx.ParseResult.GetValueForArgument(presetSaveArg);
            var res = new LauncherService(configPath).SaveActivePresetAs(name);
            Console.WriteLine(res.Message);
        });
        presetCmd.AddCommand(presetSaveCmd);

        var presetLoadArg = new Argument<string>("name", "Preset name.");
        var presetLoadCmd = new Command("load", "Make a preset active.") { presetLoadArg };
        presetLoadCmd.SetHandler(ctx =>
            Activate(c, ctx, ctx.ParseResult.GetValueForArgument(presetLoadArg)));
        presetCmd.AddCommand(presetLoadCmd);

        var presetRmArg = new Argument<string>("name", "Preset name.");
        var presetRmCmd = new Command("rm", "Delete a preset.") { presetRmArg };
        presetRmCmd.SetHandler(ctx =>
        {
            var (_, _, active, configPath) = c.Resolve(ctx);
            var name = ctx.ParseResult.GetValueForArgument(presetRmArg);
            if (Profiles.Delete(name, configPath))
            {
                if (active == name) Profiles.SetActive("", configPath);
                Console.WriteLine($"deleted preset '{name}'");
            }
            else
            {
                CliOut.Fail(ctx, $"no preset '{name}'");
            }
        });
        presetCmd.AddCommand(presetRmCmd);
        return presetCmd;
    }
}
