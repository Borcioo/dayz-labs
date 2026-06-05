using Dzl.Core.Config;

namespace Dzl.Core.Build;

/// <summary>Outcome of a mod build. <see cref="Ok"/> false means the PBO was not produced (or a
/// pre-flight check failed); <see cref="Message"/> is a one-line summary and <see cref="Output"/>
/// holds the captured AddonBuilder log.</summary>
public sealed record BuildResult(
    bool Ok, string ModName, string ModDir, string PboPath, bool Registered, string Message, string Output);

/// <summary>
/// Side-effect-light helpers for the build→deploy pipeline: verifying a fresh PBO was produced, the
/// ownership marker, and the run-list registration. Output paths now live in <c>ProjectPaths</c>
/// (builds are physical under <c>&lt;ProjectsRoot&gt;\build\@&lt;Mod&gt;</c>); the I/O orchestration
/// (junctions, AddonBuilder process, save) lives in <c>Dzl.Core.App.BuildService</c>.
/// </summary>
public static class ModBuild
{
    /// <summary>Newest <c>.pbo</c> in the Addons dir, or null if none exists.</summary>
    public static FileInfo? NewestPbo(string addonsDir)
    {
        if (!Directory.Exists(addonsDir)) return null;
        return new DirectoryInfo(addonsDir)
            .EnumerateFiles("*.pbo", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>True when a <c>.pbo</c> exists that was written at/after the build started — proof the
    /// builder actually produced output rather than silently no-op'ing on an old file.</summary>
    public static bool HasFreshPbo(string addonsDir, DateTime sinceUtc) =>
        NewestPbo(addonsDir) is { } f && f.LastWriteTimeUtc >= sinceUtc;

    /// <summary>Drops the dzl ownership marker (at <paramref name="markerPath"/>) so later runs know this
    /// build folder is dzl-built and safe to overwrite/clean.</summary>
    public static void WriteMarker(string markerPath, string detail)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, detail);
    }

    /// <summary>Append the built mod's load path to the run-list (enabled, both sides), deduping on path
    /// case-insensitively so re-builds don't pile up duplicates. Pure: returns an updated config.</summary>
    public static DzlConfig Register(DzlConfig cfg, string loadPath, string side = "both")
    {
        if (cfg.Mods.Any(m => string.Equals(m.Path, loadPath, StringComparison.OrdinalIgnoreCase)))
            return cfg;
        var mods = new List<ModEntry>(cfg.Mods) { new() { Path = loadPath, Enabled = true, Side = side } };
        return cfg with { Mods = mods };
    }
}
