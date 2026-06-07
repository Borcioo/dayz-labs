namespace Dzl.Core.Economy;

public sealed record CeSnapshot(string Id, DateTime Created, string Path);

/// <summary>Versioned snapshots of any Central Economy file. Keeps the newest <see cref="Keep"/>.
/// On-disk layout is intentionally compatible with <see cref="TypesBackup"/>: backups land in
/// <c>.dzl-&lt;stem&gt;-backups\</c> next to the file, named <c>&lt;stem&gt;.&lt;id&gt;&lt;ext&gt;</c>.
/// For <c>types.xml</c> this produces the same folder and filenames as TypesBackup used to, so
/// existing user backups remain visible after the migration.
/// Never throws — best-effort I/O.</summary>
public static class CeBackup
{
    public const int Keep = 20;

    private static int _seq;   // uniquifier so two snapshots within the same millisecond don't collide

    internal static string BackupDir(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(dir, $".dzl-{stem}-backups");
    }

    private static string Stem(string filePath) => Path.GetFileNameWithoutExtension(filePath);
    private static string Ext(string filePath)  => Path.GetExtension(filePath);

    // -------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------

    /// <summary>Snapshot the current file (if any). Prunes the backup folder to <see cref="Keep"/> afterwards.</summary>
    public static void Snapshot(string filePath) => SnapshotWithId(filePath, id: null);

    /// <summary>Snapshot with a caller-supplied id (used by <see cref="TypesBackup"/> for deterministic test stamps).
    /// When <paramref name="id"/> is null a unique timestamped id is generated.</summary>
    internal static string SnapshotWithId(string filePath, string? id)
    {
        try
        {
            if (!File.Exists(filePath)) return "";
            var dir = BackupDir(filePath);
            Directory.CreateDirectory(dir);

            if (id is null)
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var n = System.Threading.Interlocked.Increment(ref _seq);
                id = $"{stamp}-{n}";
            }

            var dst = Path.Combine(dir, $"{Stem(filePath)}.{id}{Ext(filePath)}");
            // Avoid clobbering when caller reuses the same id within a test.
            for (var i = 1; File.Exists(dst); i++)
                dst = Path.Combine(dir, $"{Stem(filePath)}.{id}-{i}{Ext(filePath)}");

            File.Copy(filePath, dst);
            Prune(filePath, dir);
            return dst;
        }
        catch { return ""; }
    }

    /// <summary>All snapshots for <paramref name="filePath"/>, newest first.</summary>
    public static List<CeSnapshot> List(string filePath)
    {
        try
        {
            var dir = BackupDir(filePath);
            if (!Directory.Exists(dir)) return new();
            var stem = Stem(filePath);
            var ext  = Ext(filePath);
            var prefix = $"{stem}.";
            return Directory.EnumerateFiles(dir, $"{prefix}*{ext}")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Select(f =>
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(f);
                    var id = nameNoExt[prefix.Length..];
                    var created = File.GetLastWriteTime(f);
                    return new CeSnapshot(id, created, f);
                })
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Restore a snapshot (by id) over the live file.
    /// Snapshots the current file first so the restore is undoable.
    /// Returns false when the id is not found or an error occurs.</summary>
    public static bool Restore(string filePath, string id)
    {
        try
        {
            var dir = BackupDir(filePath);
            var backupFile = Path.Combine(dir, $"{Stem(filePath)}.{id}{Ext(filePath)}");
            if (!File.Exists(backupFile)) return false;
            Snapshot(filePath);
            File.Copy(backupFile, filePath, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    // -------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------

    private static void Prune(string filePath, string dir)
    {
        var stem   = Stem(filePath);
        var ext    = Ext(filePath);
        var files  = Directory.EnumerateFiles(dir, $"{stem}.*{ext}")
            .OrderByDescending(f => f, StringComparer.Ordinal).ToList();
        foreach (var old in files.Skip(Keep))
            try { File.Delete(old); } catch { /* best-effort */ }
    }
}
