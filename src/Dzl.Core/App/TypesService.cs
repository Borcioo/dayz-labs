using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;

namespace Dzl.Core.App;

public sealed record TypesOp(bool Ok, string Message);

/// <summary>One Types entry plus the origin/source of the CE file it came from (for source-aware
/// listing/filtering in the CLI/MCP/tray).</summary>
public sealed record TypeRow(TypeEntry Entry, CeOrigin Origin, string ModSource);

/// <summary>
/// Central Economy editor over ALL Types files of the active server instance's mission — the vanilla
/// <c>db\types.xml</c> plus every <c>type="types"</c> file referenced from <c>cfgeconomycore.xml</c>.
/// Reads union entries (each stamped with its <see cref="TypeEntry.SourceFile"/>); writes route each
/// entry back to its own file. Every write snapshots a versioned backup (<see cref="CeBackup"/>) and
/// edits in place (preserving comments/order via <see cref="TypesXml"/>). One facade per frontend.
/// </summary>
public sealed class TypesService
{
    private readonly string _configPath;
    public TypesService(string configPath) { _configPath = configPath; }

    // ------------------------------------------------------------------
    // File resolution
    // ------------------------------------------------------------------

    private MissionPaths? Mission()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return MissionLocator.Resolve(cfg);
    }

    /// <summary>All Types <see cref="CeFileRef"/>s for the active mission that exist on disk: the files
    /// referenced by <c>cfgeconomycore.xml</c> (filtered to Types), plus vanilla <c>db/types.xml</c> as
    /// <see cref="CeOrigin.Vanilla"/> if present and not already referenced. Empty when no mission.</summary>
    private List<CeFileRef> ResolveTypesFiles() => ResolveAll().Files;

    /// <summary>Resolves the Types file list and primary path in one config read.
    /// Primary = vanilla db/types.xml when present, else first resolved file.</summary>
    private (List<CeFileRef> Files, string? Primary) ResolveAll()
    {
        var mp = Mission();
        var files = new List<CeFileRef>();
        if (mp is not null)
        {
            if (File.Exists(mp.EconomyCore))
            {
                try
                {
                    files.AddRange(EconomyCore.Parse(File.ReadAllText(mp.EconomyCore), mp.MissionDir)
                        .Where(r => r.Kind == CeKind.Types));
                }
                catch { /* malformed cfgeconomycore.xml → fall back to vanilla only */ }
            }

            if (mp.Vanilla is not null &&
                !files.Any(r => string.Equals(r.Path, mp.Vanilla, StringComparison.OrdinalIgnoreCase)))
            {
                files.Insert(0, new CeFileRef(mp.Vanilla, CeKind.Types, CeOrigin.Vanilla, "vanilla"));
            }

            files = files.Where(r => File.Exists(r.Path)).ToList();
        }

        string? primary = mp?.Vanilla ?? files.FirstOrDefault()?.Path;
        return (files, primary);
    }

    /// <summary>The active mission's primary types file (vanilla db\types.xml, or the first resolved
    /// Types file). Kept for back-compat with callers that probe a single "the types file".</summary>
    public string? TypesFile() => ResolveAll().Primary;

    // ------------------------------------------------------------------
    // Read
    // ------------------------------------------------------------------

    /// <summary>Union of every resolved Types file's entries, each stamped with its source file path.
    /// Per-file parse errors are skipped.</summary>
    public List<TypeEntry> List() => Rows().Select(r => r.Entry).ToList();

    /// <summary>Like <see cref="List"/> but each entry carries the origin + mod source of its file.</summary>
    public List<TypeRow> Rows()
    {
        var rows = new List<TypeRow>();
        foreach (var fileRef in ResolveTypesFiles())
        {
            List<TypeEntry> entries;
            try { entries = TypesXml.Parse(File.ReadAllText(fileRef.Path)); }
            catch { continue; }   // skip the bad file
            foreach (var e in entries)
                rows.Add(new TypeRow(e with { SourceFile = fileRef.Path }, fileRef.Origin, fileRef.ModSource));
        }
        return rows;
    }

    /// <summary>Valid usage/value/tag/category names from the mission's <c>cfglimitsdefinition.xml</c>
    /// (<see cref="LimitsDef.Empty"/> when absent).</summary>
    public LimitsDef Limits()
    {
        var mp = Mission();
        if (mp is null) return LimitsDef.Empty;
        var path = Path.Combine(mp.MissionDir, "cfglimitsdefinition.xml");
        if (!File.Exists(path)) return LimitsDef.Empty;
        try { return LimitsXml.Parse(File.ReadAllText(path)); }
        catch { return LimitsDef.Empty; }
    }

    /// <summary>Run the CE lint rules over the full multi-file Types set against the mission's limits.</summary>
    public IReadOnlyList<LintFinding> Lint() => new LintEngine().Run(new CeFileSet(List()), Limits());

    // ------------------------------------------------------------------
    // Write
    // ------------------------------------------------------------------

    /// <summary>Sync every resolved Types file to the edited set: entries are grouped by their
    /// <see cref="TypeEntry.SourceFile"/> (empty → the primary file; a path not in the resolved CE
    /// set → the primary file, preventing writes to stray/orphan paths the server never loads).
    /// Each target file is loaded (or created as <c>&lt;types/&gt;</c>), pruned of types no longer
    /// kept for it, upserted, snapshotted, and written back. Per-file try/catch; ok only when no
    /// file failed.</summary>
    public TypesOp SaveAll(IReadOnlyList<TypeEntry> entries)
    {
        var (resolved, primary) = ResolveAll();   // I1: resolve once
        if (primary is null) return new(false, "no types.xml files resolved");

        // C1: build the set of valid write targets; route orphan SourceFile paths to primary.
        var valid = new HashSet<string>(resolved.Select(r => r.Path), StringComparer.OrdinalIgnoreCase) { primary };
        var groups = entries.GroupBy(e =>
            string.IsNullOrEmpty(e.SourceFile) ? primary
            : valid.Contains(e.SourceFile) ? e.SourceFile
            : primary,
            StringComparer.OrdinalIgnoreCase);

        var saved = new List<string>();
        var failed = new List<string>();
        foreach (var g in groups)
        {
            var file = g.Key;
            try
            {
                var doc = File.Exists(file)
                    ? TypesXml.ParseDoc(File.ReadAllText(file))
                    : new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("types"));
                var keep = new HashSet<string>(g.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var t in doc.Root!.Elements("type").ToList())
                    if (!keep.Contains(t.Attribute("name")?.Value ?? "")) t.Remove();
                foreach (var e in g) TypesXml.Upsert(doc, e);
                CeBackup.Snapshot(file);
                File.WriteAllText(file, TypesXml.ToXml(doc));
                saved.Add(Path.GetFileName(file));
            }
            catch { failed.Add(Path.GetFileName(file)); }
        }

        // I4: informative partial result
        if (failed.Count == 0)
            return new(true, $"saved {entries.Count} types across {saved.Count} file(s)");
        if (saved.Count > 0)
            return new(false, $"partial save: {saved.Count} ok, failed: {string.Join(", ", failed)}");
        return new(false, $"failed: {string.Join(", ", failed)}");
    }

    /// <summary>Set/insert one type with nullable overrides (CLI/MCP). Writes to the entry's existing
    /// source file if the type already exists, else to <paramref name="file"/> if given, else the primary
    /// file. Unspecified fields keep their current value (or the default for a new entry).
    /// <para><b>Not concurrency-safe:</b> concurrent calls may interleave reads and writes; callers
    /// must serialize if needed.</para></summary>
    public TypesOp Set(string name, int? nominal = null, int? min = null, int? lifetime = null,
                       int? restock = null, int? cost = null, string? category = null, string? file = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return new(false, "type name required");

        var (_, primary) = ResolveAll();   // I1: resolve once
        var existing = Rows().FirstOrDefault(r => string.Equals(r.Entry.Name, name, StringComparison.OrdinalIgnoreCase));
        var target = existing?.Entry.SourceFile ?? file ?? primary;
        if (string.IsNullOrEmpty(target)) return new(false, "no types.xml for the active server's mission");

        try
        {
            var doc = File.Exists(target)
                ? TypesXml.ParseDoc(File.ReadAllText(target))
                : new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("types"));
            var el = doc.Root!.Elements("type")
                .FirstOrDefault(t => string.Equals(t.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
            var cur = el != null ? TypesXml.ReadType(el) : new TypeEntry { Name = name };
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
            CeBackup.Snapshot(target);
            TypesXml.Upsert(doc, merged);
            File.WriteAllText(target, TypesXml.ToXml(doc));
            return new(true, el != null ? $"updated {name}" : $"added {name}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>Remove the named type from whichever resolved file(s) contain it (each affected file is
    /// snapshotted first).</summary>
    public TypesOp Remove(string name)
    {
        var files = ResolveTypesFiles();
        if (files.Count == 0) return new(false, "no types.xml for the active server's mission");

        var removed = 0;
        var failed = new List<string>();
        foreach (var fileRef in files)
        {
            try
            {
                var doc = TypesXml.ParseDoc(File.ReadAllText(fileRef.Path));
                if (!TypesXml.Remove(doc, name)) continue;
                CeBackup.Snapshot(fileRef.Path);
                File.WriteAllText(fileRef.Path, TypesXml.ToXml(doc));
                removed++;
            }
            catch { failed.Add(Path.GetFileName(fileRef.Path)); }
        }

        if (failed.Count > 0) return new(false, $"failed: {string.Join(", ", failed)}");
        return removed > 0 ? new(true, $"removed {name}") : new(false, $"no such type: {name}");
    }

    // ------------------------------------------------------------------
    // Backups / restore (primary file)
    // ------------------------------------------------------------------

    /// <summary>Lists versioned backups for the <b>primary</b> types file only (vanilla
    /// <c>db\types.xml</c> or the first resolved file). Mod/custom CE files maintain their own
    /// backup snapshots in <c>.dzl-&lt;stem&gt;-backups/</c> directories next to each file; full
    /// multi-file backup browsing is a future sub-project.</summary>
    public List<TypesBackupInfo> Backups()
    {
        var f = ResolveAll().Primary;
        return f is null ? new() : TypesBackup.List(f);
    }

    /// <summary>Restores the <b>primary</b> types file from a specific backup snapshot. Operates
    /// on the primary file only (vanilla <c>db\types.xml</c> or the first resolved file). Mod/custom
    /// CE files maintain their own backup snapshots in <c>.dzl-&lt;stem&gt;-backups/</c> directories
    /// next to each file; full multi-file restore browsing is a future sub-project.</summary>
    public TypesOp Restore(string backupFile)
    {
        var f = ResolveAll().Primary;
        if (f is null) return new(false, "no types.xml for the active server's mission");
        return TypesBackup.Restore(f, backupFile)
            ? new(true, $"restored {Path.GetFileName(backupFile)}")
            : new(false, "restore failed");
    }
}
