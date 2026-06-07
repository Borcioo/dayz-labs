using Dzl.Core.Config;
using Dzl.Core.Projects;

namespace Dzl.Core.Mods;

/// <summary>What a discovered mod folder actually is, by where it lives on disk.</summary>
public enum ModKind
{
    /// <summary>Your own mod, uncompiled source (under &lt;root&gt;\mods or the P: source link).</summary>
    Source,
    /// <summary>Your own mod, compiled output (under &lt;root&gt;\build or P:\Mods\@…).</summary>
    Build,
    /// <summary>Subscribed in the Steam client (its workshop content folder).</summary>
    Workshop,
    /// <summary>Downloaded via steamcmd (the &lt;root&gt;\workshop folder).</summary>
    Downloaded,
    /// <summary>Anything outside the known structure.</summary>
    External,
}

/// <summary>Classifies a mod folder into a <see cref="ModKind"/> purely from its path. Pure + unit-tested.</summary>
public static class ModClassify
{
    public static ModKind Classify(string? path, DzlConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(path)) return ModKind.External;
        var root = ProjectPaths.Root(cfg);
        var workshopDir = string.IsNullOrWhiteSpace(cfg.WorkshopDir) ? ProjectPaths.WorkshopDir(root) : cfg.WorkshopDir;
        var steam = Dzl.Core.Env.EnvDetect.SteamPath();

        // Order matters: more specific prefixes first (P:\Mods before P:\, build before mods).
        if (steam is not null && Under(path, Path.Combine(steam, "steamapps", "workshop", "content", WorkshopCmdAppId))) return ModKind.Workshop;
        if (Under(path, workshopDir)) return ModKind.Downloaded;
        if (Under(path, ProjectPaths.BuildRoot(root)) || Under(path, @"P:\Mods")) return ModKind.Build;
        if (Under(path, ProjectPaths.ModsDir(root)) || Under(path, @"P:\")) return ModKind.Source;
        return ModKind.External;
    }

    private const string WorkshopCmdAppId = "221100";

    /// <summary>True when <paramref name="path"/> is <paramref name="dir"/> or sits inside it. Case-insensitive,
    /// separator- and trailing-slash-insensitive.</summary>
    private static bool Under(string path, string dir)
    {
        var p = Norm(path);
        var d = Norm(dir);
        return d.Length > 0 && (string.Equals(p, d, StringComparison.OrdinalIgnoreCase)
                                || p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private static string Norm(string s) => s.Replace('/', '\\').TrimEnd('\\');
}
