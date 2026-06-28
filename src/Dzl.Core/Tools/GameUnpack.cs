using System.Text.Json;
using Dzl.Core.Procs;

namespace Dzl.Core.Tools;

/// <summary>One unpacked PBO's progress line.</summary>
public sealed record UnpackItem(int Index, int Total, string Pbo, string Status);

/// <summary>Outcome of a full game-data unpack.</summary>
public sealed record UnpackResult(bool Ok, int Total, int Extracted, int Skipped, int Failed, string Message);

/// <summary>
/// Reliable vanilla game-data extraction: drives DayZ Tools' <c>BankRev.exe</c> over EVERY game PBO, unpacking
/// each faithfully to the project drive at its own prefix (<c>BankRev -f P:\ -prefix &lt;pbo&gt;</c>). This is the
/// drop-in for <c>WorkDrive.exe /ExtractGameData</c>, whose built-in extraction is unreliable — it creates the
/// folders but silently skips files (e.g. <c>dz\worlds\enoch\data\enoch_riversea.emat</c>), which then read as
/// "missing" to preflight/binarize even though they exist in the game's PBOs. Unpacking each PBO individually with
/// BankRev avoids that. Incremental by default (a manifest skips PBOs already extracted at their current
/// timestamp+size); <c>force</c> re-extracts everything. Never throws.
/// </summary>
public static class GameUnpack
{
    /// <summary>The manifest of what's been extracted, kept at the destination root so it travels with the drive.</summary>
    public const string ManifestName = ".dzl-extracted.json";

    /// <summary><c>-f &lt;dest&gt; -prefix &lt;pbo&gt;</c> — extract the PBO under <paramref name="destRoot"/> at its
    /// own PBO prefix (so <c>worlds_enoch.pbo</c> lands at <c>&lt;dest&gt;\dz\worlds\enoch\…</c>). Pure.</summary>
    public static List<string> BankRevArgs(string pbo, string destRoot) =>
        new() { "-f", destRoot, "-prefix", pbo };

    /// <summary>Identity stamp for a PBO: last-write + size. Re-extract only when this changes. Pure.</summary>
    public static string Stamp(FileInfo pbo) => $"{pbo.LastWriteTimeUtc.Ticks}:{pbo.Length}";

    /// <summary>Every <c>*.pbo</c> under the DayZ install (Addons, sakhal\Addons, any DLC), sorted. The install
    /// holds only vanilla PBOs (workshop mods live elsewhere), so all of them are game data to unpack.</summary>
    public static IReadOnlyList<string> FindGamePbos(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(gameDir, "*.pbo", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Unpack every game PBO into <paramref name="destRoot"/> (the mounted project drive, e.g. <c>P:\</c>).
    /// Reports each PBO via <paramref name="onItem"/>; honours <paramref name="cancelled"/> between PBOs.</summary>
    public static UnpackResult UnpackAll(string bankRevExe, string gameDir, string destRoot, bool force,
        Action<UnpackItem>? onItem = null, Func<bool>? cancelled = null)
    {
        if (string.IsNullOrWhiteSpace(bankRevExe) || !File.Exists(bankRevExe))
            return new UnpackResult(false, 0, 0, 0, 0, "BankRev.exe not found — set the DayZ Tools path");
        if (!Directory.Exists(destRoot))
            return new UnpackResult(false, 0, 0, 0, 0, $"destination not available: {destRoot} (mount P: first)");

        var pbos = FindGamePbos(gameDir);
        if (pbos.Count == 0)
            return new UnpackResult(false, 0, 0, 0, 0, $"no PBOs found under {gameDir}");

        var manifestPath = Path.Combine(destRoot, ManifestName);
        var manifest = force ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : LoadManifest(manifestPath);

        int extracted = 0, skipped = 0, failed = 0;
        for (var i = 0; i < pbos.Count; i++)
        {
            if (cancelled?.Invoke() == true)
                return new UnpackResult(false, pbos.Count, extracted, skipped, failed,
                    $"cancelled after {extracted + skipped + failed}/{pbos.Count}");

            var pbo = pbos[i];
            var idx = i + 1;
            var stamp = Stamp(new FileInfo(pbo));
            if (!force && manifest.TryGetValue(pbo, out var prev) && prev == stamp)
            {
                skipped++;
                onItem?.Invoke(new UnpackItem(idx, pbos.Count, pbo, "up to date"));
                continue;
            }

            var r = ProcRunner.Run(bankRevExe, BankRevArgs(pbo, destRoot),
                new RunOpts(WorkingDir: Path.GetDirectoryName(bankRevExe), TimeoutMs: 10 * 60 * 1000));
            if (r.Ok)
            {
                extracted++;
                manifest[pbo] = stamp;
                onItem?.Invoke(new UnpackItem(idx, pbos.Count, pbo, "extracted"));
                SaveManifest(manifestPath, manifest);   // persist incrementally so a crash/cancel keeps progress
            }
            else
            {
                failed++;
                onItem?.Invoke(new UnpackItem(idx, pbos.Count, pbo, $"FAILED: {Trim(r.AllOutput)}"));
            }
        }

        var ok = failed == 0;
        return new UnpackResult(ok, pbos.Count, extracted, skipped, failed,
            $"{(ok ? "done" : "completed with errors")}: {extracted} extracted, {skipped} up-to-date, {failed} failed");
    }

    private static string Trim(string s) => s.Length <= 200 ? s.Trim() : s.Substring(0, 200).Trim() + "…";

    private static Dictionary<string, string> LoadManifest(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (d is not null) return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* corrupt/unreadable manifest — treat as empty (everything re-extracts) */ }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveManifest(string path, Dictionary<string, string> manifest)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* best-effort — next run just re-extracts */ }
    }
}
