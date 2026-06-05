using Dzl.Core.Config;

namespace Dzl.Core.Build;

/// <summary>Outcome of a mod build. <see cref="Ok"/> false means the PBO was not produced (or a
/// pre-flight check failed); <see cref="Message"/> is a one-line summary and <see cref="Output"/>
/// holds the captured AddonBuilder log.</summary>
public sealed record BuildResult(
    bool Ok, string ModName, string ModDir, string PboPath, bool Registered, string Message, string Output);

/// <summary>
/// Pure helpers for the build→deploy pipeline: where a built mod lands on the work drive, how to
/// verify a fresh PBO was produced, the ownership marker, and the run-list registration. The I/O
/// orchestration (junction, AddonBuilder process, save) lives in <c>Dzl.Core.App.BuildService</c>;
/// everything here is side-effect-light and unit-tested.
/// </summary>
public static class ModBuild
{
    /// <summary>Built mods are deployed under <c>P:\Mods\@&lt;Mod&gt;\Addons\</c> so the dev engine can
    /// load them from the work drive (matches the agentic-z layout, rooted on P:).</summary>
    public const string ModsRoot = @"P:\Mods";

    public static string OutputDir(string mod) => Path.Combine(ModsRoot, "@" + mod);
    public static string AddonsDir(string mod) => Path.Combine(OutputDir(mod), "Addons");
    public static string MarkerPath(string mod) => Path.Combine(OutputDir(mod), ".dzl-build");

    /// <summary>The path written into the run-list — the engine loads the <c>@&lt;Mod&gt;</c> folder.</summary>
    public static string LoadPath(string mod) => OutputDir(mod);

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

    /// <summary>Drops the dzl ownership marker so later runs know this <c>@&lt;Mod&gt;</c> is dzl-built
    /// (and safe to overwrite/clean), mirroring agentic-z's scaffold marker.</summary>
    public static void WriteMarker(string mod, string detail)
    {
        Directory.CreateDirectory(OutputDir(mod));
        File.WriteAllText(MarkerPath(mod), detail);
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
