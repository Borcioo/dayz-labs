namespace Dzl.Core.Economy;

public sealed record TypesBackupInfo(string Stamp, string Path);

/// <summary>Versioned backups for an edited types.xml: each save snapshots the file into a
/// <c>.dzl-types-backups\</c> folder next to it (<c>types.&lt;timestamp&gt;.xml</c>), keeping the newest
/// <see cref="Keep"/>. Restore rolls back to any snapshot (snapshotting the current file first, so a
/// restore is itself undoable). Never throws — failures degrade to "no backup".</summary>
public static class TypesBackup
{
    public const string DirName = ".dzl-types-backups";
    public const int Keep = 20;
    private const string Prefix = "types.";
    private const string Ext = ".xml";

    public static string BackupDir(string typesPath) =>
        Path.Combine(Path.GetDirectoryName(typesPath) ?? ".", DirName);

    /// <summary>Snapshot the current file (if any). Returns the snapshot path, or "" if nothing to back up.
    /// <paramref name="stamp"/> lets callers pass a deterministic timestamp (tests); null = now.</summary>
    public static string Snapshot(string typesPath, string? stamp = null)
    {
        try
        {
            if (!File.Exists(typesPath)) return "";
            var dir = BackupDir(typesPath);
            Directory.CreateDirectory(dir);
            stamp ??= DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dst = Path.Combine(dir, $"{Prefix}{stamp}{Ext}");
            // Avoid clobbering within the same second.
            for (var i = 1; File.Exists(dst); i++) dst = Path.Combine(dir, $"{Prefix}{stamp}-{i}{Ext}");
            File.Copy(typesPath, dst);
            Prune(dir);
            return dst;
        }
        catch { return ""; }
    }

    /// <summary>Snapshots newest-first.</summary>
    public static List<TypesBackupInfo> List(string typesPath)
    {
        try
        {
            var dir = BackupDir(typesPath);
            if (!Directory.Exists(dir)) return new();
            return Directory.EnumerateFiles(dir, $"{Prefix}*{Ext}")
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .Select(f => new TypesBackupInfo(
                    Path.GetFileNameWithoutExtension(f)[Prefix.Length..], f))
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Restore a snapshot over the live file, snapshotting the current file first so it's undoable.</summary>
    public static bool Restore(string typesPath, string backupFile)
    {
        try
        {
            if (!File.Exists(backupFile)) return false;
            Snapshot(typesPath);
            File.Copy(backupFile, typesPath, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    private static void Prune(string dir)
    {
        var files = Directory.EnumerateFiles(dir, $"{Prefix}*{Ext}")
            .OrderByDescending(f => f, StringComparer.Ordinal).ToList();
        foreach (var old in files.Skip(Keep))
            try { File.Delete(old); } catch { /* best-effort */ }
    }
}
