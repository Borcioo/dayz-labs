using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;

namespace Dzl.Core.App;

/// <summary>One Types entry plus the origin/source of the CE file it came from (for source-aware
/// listing/filtering in the CLI/MCP/tray).</summary>
public sealed record TypeRow(TypeEntry Entry, CeOrigin Origin, string ModSource);

/// <summary>
/// Central Economy editor over ALL Types files of the active mission — the vanilla <c>db\types.xml</c>
/// plus every <c>type="types"</c> file referenced from <c>cfgeconomycore.xml</c>.
/// </summary>
/// <remarks>Reads union entries (each stamped with its <see cref="TypeEntry.SourceFile"/>); writes
/// route each entry back to its own file. Every write snapshots a versioned backup
/// (<see cref="CeBackup"/>) and edits in place so comments/order survive a round-trip.</remarks>
public sealed class TypesService
{
    private readonly string _configPath;
    public TypesService(string configPath) { _configPath = configPath; }

    private MissionPaths? Mission()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return MissionLocator.Resolve(cfg);
    }

    private List<CeFileRef> ResolveTypesFiles() => ResolveAll().Files;

    // Primary = vanilla db/types.xml when present, else the first resolved file.
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
            try { entries = TypesXml.Parse(File.ReadAllText(fileRef.Path), fileRef.Path); }
            catch { continue; }   // skip the bad file
            foreach (var e in entries)
                rows.Add(new TypeRow(e, fileRef.Origin, fileRef.ModSource));
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

    /// <summary>Sync every resolved Types file to the edited set: entries are grouped by their
    /// <see cref="TypeEntry.SourceFile"/> and each target file is pruned, upserted, snapshotted,
    /// and written back. Ok only when no file failed.</summary>
    /// <remarks>An empty or orphan SourceFile (a path not in the resolved CE set) routes to the
    /// primary file, preventing writes to stray paths the server never loads.</remarks>
    public OpResult SaveAll(IReadOnlyList<TypeEntry> entries)
    {
        var (resolved, primary) = ResolveAll();
        if (primary is null) return new(false, "no types.xml files resolved");

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
    public OpResult Set(string name, int? nominal = null, int? min = null, int? lifetime = null,
                       int? restock = null, int? cost = null, string? category = null, string? file = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return new(false, "type name required");

        var (resolved, primary) = ResolveAll();

        // Probe in resolved order — the first file containing the name wins. Parsed docs are kept
        // so the edit below reuses them; malformed files are skipped exactly like Rows() skips them.
        var probed = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        string? target = null;
        XElement? el = null;
        foreach (var fileRef in resolved)
        {
            XDocument candidate;
            try { candidate = TypesXml.ParseDoc(File.ReadAllText(fileRef.Path)); }
            catch { continue; }   // skip the bad file
            probed[fileRef.Path] = candidate;
            var match = candidate.Root?.Elements("type").ByName(name);
            if (match is null) continue;
            target = fileRef.Path;
            el = match;
            break;
        }

        // Target: the file the existing type came from, else the file param matched against the
        // resolved CE list (by basename or full path, so "mymod_types.xml" lands in the real
        // CE-registered file, not CWD), else the primary types file.
        if (target is null)
        {
            if (!string.IsNullOrEmpty(file))
            {
                var matched = resolved.FirstOrDefault(r =>
                    Path.GetFileName(r.Path).Equals(file, StringComparison.OrdinalIgnoreCase) ||
                    r.Path.Equals(file, StringComparison.OrdinalIgnoreCase));
                target = matched?.Path ?? primary;
            }
            else
            {
                target = primary;
            }
        }

        if (string.IsNullOrEmpty(target)) return new(false, "no types.xml for the active server's mission");

        try
        {
            var doc = probed.TryGetValue(target, out var reused)
                ? reused
                : File.Exists(target)
                    ? TypesXml.ParseDoc(File.ReadAllText(target))
                    : new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("types"));
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
    public OpResult Remove(string name)
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

    /// <summary>Lists versioned backups for the <b>primary</b> types file only.</summary>
    /// <remarks>Mod/custom CE files maintain their own snapshots in <c>.dzl-&lt;stem&gt;-backups/</c>
    /// next to each file; full multi-file backup browsing is a future sub-project.</remarks>
    public List<TypesBackupInfo> Backups()
    {
        var f = ResolveAll().Primary;
        return f is null ? new() : TypesBackup.List(f);
    }

    /// <summary>Restores the <b>primary</b> types file from a specific backup snapshot
    /// (see <see cref="Backups"/> for the primary-only scope).</summary>
    public OpResult Restore(string backupFile)
    {
        var f = ResolveAll().Primary;
        if (f is null) return new(false, "no types.xml for the active server's mission");
        return TypesBackup.Restore(f, backupFile)
            ? new(true, $"restored {Path.GetFileName(backupFile)}")
            : new(false, "restore failed");
    }
}
