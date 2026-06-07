namespace Dzl.Core.Economy;

public sealed record TypesBackupInfo(string Stamp, string Path);

/// <summary>Versioned backups for an edited types.xml. Delegates to <see cref="CeBackup"/> so
/// all CE files share the same snapshot engine, while preserving the existing on-disk layout
/// (<c>.dzl-types-backups\types.&lt;stamp&gt;.xml</c>) and public API so callers and tests are unaffected.</summary>
public static class TypesBackup
{
    public const int Keep = CeBackup.Keep;

    public static string BackupDir(string typesPath) =>
        CeBackup.BackupDir(typesPath);

    /// <summary>Snapshot the current file (if any). Returns the snapshot path, or "" if nothing to back up.
    /// <paramref name="stamp"/> lets callers pass a deterministic timestamp (tests); null = now.</summary>
    public static string Snapshot(string typesPath, string? stamp = null) =>
        CeBackup.SnapshotWithId(typesPath, stamp);

    /// <summary>Snapshots newest-first.</summary>
    public static List<TypesBackupInfo> List(string typesPath) =>
        CeBackup.List(typesPath)
            .Select(s => new TypesBackupInfo(s.Id, s.Path))
            .ToList();

    /// <summary>Restore a snapshot over the live file, snapshotting the current file first so it's undoable.
    /// <paramref name="backupFile"/> may be a full path (as returned by <see cref="List"/> or <see cref="CeBackup.List"/>)
    /// or a bare backup filename — both forms are accepted.</summary>
    public static bool Restore(string typesPath, string backupFile)
    {
        // Extract the id from the backup file name: derive the filename portion first (handles full paths),
        // then strip "<stem>." prefix and extension.
        var stem   = Path.GetFileNameWithoutExtension(typesPath);
        var name   = Path.GetFileNameWithoutExtension(Path.GetFileName(backupFile));
        var prefix = $"{stem}.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var id = name[prefix.Length..];
        return CeBackup.Restore(typesPath, id);
    }
}
