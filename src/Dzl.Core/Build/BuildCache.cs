using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dzl.Core.Build.Preflight;
using Dzl.Core.Json;

namespace Dzl.Core.Build;

/// <summary>
/// Skip-unchanged support: a per-mod state hash over the packable payload + the settings that
/// affect the output. Content-based (sha1 of file bytes), not timestamp-based — git checkouts
/// touch mtimes without changing content, and a stale-positive cache that skips wrongly is
/// worse than no cache. A per-run memo keyed by (path, size, mtime) keeps rehashing cheap.
/// </summary>
public static class BuildCache
{
    public sealed record Entry(string Hash, string Pbo, DateTime UpdatedUtc);

    private sealed record CacheFile(Dictionary<string, Entry> Mods);

    private static readonly JsonSerializerOptions Json = DzlJson.SnakeIndented;

    public static string CachePath(string configDir) => Path.Combine(configDir, ".dzl-build-cache.json");

    public static Dictionary<string, Entry> Load(string configDir)
    {
        try
        {
            var path = CachePath(configDir);
            if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
            var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), Json);
            return data is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Entry>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Save(string configDir, Dictionary<string, Entry> cache)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(CachePath(configDir), JsonSerializer.Serialize(cache, Json));
        }
        catch { /* cache loss is harmless — next build just isn't skipped */ }
    }

    /// <summary>Hash of everything that determines the build output: each packable file's
    /// relpath + size + content sha1, plus the build-affecting settings (binarize/sign flags,
    /// signing-key fingerprint, tool exe fingerprint, prefix). Order-stable.</summary>
    public static string ComputeStateHash(string modDir, IEnumerable<string> excludePatterns,
        string settingsFingerprint, Dictionary<string, string>? sha1Memo = null)
    {
        var patterns = excludePatterns.ToArray();
        using var sha = SHA1.Create();
        void Mix(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        Mix(settingsFingerprint);

        var files = Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: PathResolver.RelativeTo(f, modDir)))
            .Where(x => x.Rel is not null && !PathResolver.IsExcluded(x.Rel!, patterns))
            .OrderBy(x => x.Rel, StringComparer.OrdinalIgnoreCase);

        foreach (var (full, rel) in files)
        {
            FileInfo fi;
            try { fi = new FileInfo(full); } catch { continue; }
            Mix(rel!.ToLowerInvariant());
            Mix(fi.Length.ToString());
            Mix(FileSha1(full, fi, sha1Memo));
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>Short identity of a file (exists/size/mtime/sha1) — used to fingerprint tool exes
    /// and the signing key so a tools update or key swap invalidates the cache.</summary>
    public static string Fingerprint(string? path, Dictionary<string, string>? sha1Memo = null)
    {
        if (string.IsNullOrEmpty(path)) return "absent";
        FileInfo fi;
        try { fi = new FileInfo(path); } catch { return "absent"; }
        if (!fi.Exists) return "absent";
        return $"{fi.Length}:{FileSha1(path, fi, sha1Memo)}";
    }

    private static string FileSha1(string path, FileInfo fi, Dictionary<string, string>? memo)
    {
        var key = $"{path.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        if (memo is not null && memo.TryGetValue(key, out var cached)) return cached;
        string hash;
        try
        {
            using var fs = File.OpenRead(path);
            hash = Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
        }
        catch { hash = "unreadable"; }
        if (memo is not null) memo[key] = hash;
        return hash;
    }
}
