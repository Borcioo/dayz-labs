using System.CommandLine;
using System.CommandLine.Invocation;
using Dzl.Core.Config;

namespace Dzl.Cli;

/// <summary>
/// Shared CLI state: the global <c>--config</c> option and active-preset resolution.
/// One instance is created in Program.cs and passed to every command factory.
/// </summary>
internal sealed class CliContext
{
    private readonly string _defaultConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dzl", "config.json");

    /// <summary>Global --config option (shared instance read inside handlers).</summary>
    public Option<string?> ConfigOption { get; } =
        new("--config", () => null, "Path to config.json");

    /// <summary>
    /// Resolve config path + ensure default preset, then resolve the active preset.
    /// Returns (cfg, savePath, active, configPath).
    /// </summary>
    public (DzlConfig cfg, string savePath, string active, string configPath) Resolve(InvocationContext ctx)
    {
        var raw = ctx.ParseResult.GetValueForOption(ConfigOption);
        var configPath = string.IsNullOrWhiteSpace(raw) ? _defaultConfig : raw;
        Profiles.EnsureDefault(configPath);
        var (cfg, savePath, active) = Profiles.ResolveActive(configPath);
        return (cfg, savePath, active, configPath);
    }
}
