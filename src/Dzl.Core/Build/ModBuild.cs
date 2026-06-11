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

    /// <summary>
    /// Atomically publish a finished build: move every <c>*.pbo</c>/<c>*.bisign</c> from
    /// <paramref name="workAddonsDir"/> into <paramref name="finalAddonsDir"/>, backing up what's
    /// there first and restoring it when anything fails. A failed rebuild therefore never leaves
    /// the loadable <c>@Mod\Addons</c> half-written (the server may be configured to load it).
    /// </summary>
    public static (bool Ok, string Detail) PublishAtomically(string workAddonsDir, string finalAddonsDir)
    {
        string[] Artifacts(string dir) =>
            Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir).Where(f =>
                      f.EndsWith(".pbo", StringComparison.OrdinalIgnoreCase) ||
                      f.EndsWith(".bisign", StringComparison.OrdinalIgnoreCase)).ToArray()
                : Array.Empty<string>();

        var fresh = Artifacts(workAddonsDir);
        if (fresh.Length == 0) return (false, $"nothing to publish in {workAddonsDir}");

        Directory.CreateDirectory(finalAddonsDir);
        var backupDir = Path.Combine(finalAddonsDir, $".backup_{DateTime.UtcNow.Ticks}");
        var backedUp = new List<(string Original, string Backup)>();
        var published = new List<string>();

        try
        {
            var existing = Artifacts(finalAddonsDir);
            if (existing.Length > 0)
            {
                Directory.CreateDirectory(backupDir);
                foreach (var f in existing)
                {
                    var b = Path.Combine(backupDir, Path.GetFileName(f));
                    File.Move(f, b);
                    backedUp.Add((f, b));
                }
            }
            foreach (var f in fresh)
            {
                var dst = Path.Combine(finalAddonsDir, Path.GetFileName(f));
                File.Move(f, dst);
                published.Add(dst);
            }
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, recursive: true);
            return (true, $"published {published.Count} file(s)");
        }
        catch (Exception ex)
        {
            // Roll back: remove whatever landed, put the originals back.
            foreach (var p in published)
                try { File.Delete(p); } catch { }
            foreach (var (original, backup) in backedUp)
                try { if (File.Exists(backup)) File.Move(backup, original, overwrite: true); } catch { }
            try { if (Directory.Exists(backupDir) && !Directory.EnumerateFileSystemEntries(backupDir).Any()) Directory.Delete(backupDir); } catch { }
            return (false, $"publish failed and previous output was restored: {ex.Message}");
        }
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
