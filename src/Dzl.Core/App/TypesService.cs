using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

public sealed record TypesOp(bool Ok, string Message);

/// <summary>
/// SP6 Central Economy editor: read + edit the active server instance's mission <c>db\types.xml</c>.
/// Every write snapshots a versioned backup (<see cref="TypesBackup"/>) and edits the file in place
/// (preserving comments/order via <see cref="TypesXml"/>). One facade per frontend (CLI/MCP/tray).
/// </summary>
public sealed class TypesService
{
    private readonly string _configPath;
    public TypesService(string configPath) { _configPath = configPath; }

    /// <summary>The active instance mission's <c>db\types.xml</c> (first match under its mpmissions), or null.</summary>
    public string? TypesFile()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var cfgPath = cfg.ConfigName;
        if (string.IsNullOrWhiteSpace(cfgPath) || !Path.IsPathRooted(cfgPath)) return null;
        var instanceDir = Path.GetDirectoryName(cfgPath);
        var mp = instanceDir is null ? null : Path.Combine(instanceDir, "mpmissions");
        if (mp is null || !Directory.Exists(mp)) return null;
        foreach (var mission in Directory.GetDirectories(mp))
        {
            var t = Path.Combine(mission, "db", "types.xml");
            if (File.Exists(t)) return t;
        }
        return null;
    }

    /// <summary>All entries from the active mission's types.xml (empty if none/unreadable).</summary>
    public List<TypeEntry> List()
    {
        var f = TypesFile();
        if (f is null || !File.Exists(f)) return new();
        try { return TypesXml.Parse(File.ReadAllText(f)); }
        catch { return new(); }
    }

    /// <summary>Sync the file to the full edited set (upsert each, drop types no longer present), snapshotting
    /// first. Used by the tray grid editor.</summary>
    public TypesOp SaveAll(IReadOnlyList<TypeEntry> entries)
    {
        var f = TypesFile();
        if (f is null) return new(false, "no types.xml for the active server's mission");
        try
        {
            var doc = TypesXml.ParseDoc(File.ReadAllText(f));
            var keep = new HashSet<string>(entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var t in doc.Root!.Elements("type").ToList())
                if (!keep.Contains(t.Attribute("name")?.Value ?? "")) t.Remove();
            foreach (var e in entries) TypesXml.Upsert(doc, e);
            TypesBackup.Snapshot(f);
            File.WriteAllText(f, TypesXml.ToXml(doc));
            return new(true, $"saved {entries.Count} types → {Path.GetFileName(f)}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>Set/insert one type with nullable field overrides (CLI/MCP). Unspecified fields keep their
    /// current value (or the default for a new entry).</summary>
    public TypesOp Set(string name, int? nominal = null, int? min = null, int? lifetime = null,
                       int? restock = null, int? cost = null, string? category = null)
    {
        var f = TypesFile();
        if (f is null) return new(false, "no types.xml for the active server's mission");
        if (string.IsNullOrWhiteSpace(name)) return new(false, "type name required");
        try
        {
            var doc = TypesXml.ParseDoc(File.ReadAllText(f));
            var existing = doc.Root!.Elements("type")
                .FirstOrDefault(t => string.Equals(t.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
            var cur = existing != null ? TypesXml.ReadType(existing) : new TypeEntry { Name = name };
            var merged = cur with
            {
                Name = name,
                Nominal = nominal ?? cur.Nominal,
                Min = min ?? cur.Min,
                Lifetime = lifetime ?? cur.Lifetime,
                Restock = restock ?? cur.Restock,
                Cost = cost ?? cur.Cost,
                Category = category ?? cur.Category,
            };
            TypesBackup.Snapshot(f);
            TypesXml.Upsert(doc, merged);
            File.WriteAllText(f, TypesXml.ToXml(doc));
            return new(true, existing != null ? $"updated {name}" : $"added {name}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public TypesOp Remove(string name)
    {
        var f = TypesFile();
        if (f is null) return new(false, "no types.xml for the active server's mission");
        try
        {
            var doc = TypesXml.ParseDoc(File.ReadAllText(f));
            if (!TypesXml.Remove(doc, name)) return new(false, $"no such type: {name}");
            TypesBackup.Snapshot(f);
            File.WriteAllText(f, TypesXml.ToXml(doc));
            return new(true, $"removed {name}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public List<TypesBackupInfo> Backups()
    {
        var f = TypesFile();
        return f is null ? new() : TypesBackup.List(f);
    }

    public TypesOp Restore(string backupFile)
    {
        var f = TypesFile();
        if (f is null) return new(false, "no types.xml for the active server's mission");
        return TypesBackup.Restore(f, backupFile)
            ? new(true, $"restored {Path.GetFileName(backupFile)}")
            : new(false, "restore failed");
    }
}
