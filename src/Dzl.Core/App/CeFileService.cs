using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Shared plumbing for the single-file CE editor facades (events/globals/spawnable types/
/// player spawns/random presets). Subclasses own only the domain verbs and declare where
/// their file lives and whether an absent file may be seeded.
/// </summary>
/// <remarks>Invariants: a <see cref="CeBackup"/> snapshot before every write, in-place edits
/// so comments/order survive a round-trip, and never throw (return ok+message).</remarks>
public abstract class CeFileService
{
    protected const string NoMissionMsg = "no mission resolved for the active server";

    protected readonly string ConfigPath;

    protected CeFileService(string configPath) => ConfigPath = configPath;

    /// <summary>File path relative to the mission dir, e.g. <c>db\events.xml</c> or
    /// <c>cfgrandompresets.xml</c>.</summary>
    protected abstract string RelativePath { get; }

    /// <summary>Root element seeded when the file is absent; null = edits require an existing file.</summary>
    protected abstract string? SeedRootName { get; }

    protected MissionPaths? Mission()
    {
        var (cfg, _, _) = Profiles.ResolveActive(ConfigPath);
        return MissionLocator.Resolve(cfg);
    }

    /// <summary>The mission file's path (whether or not it exists yet), or null when no mission
    /// is resolvable.</summary>
    public string? FilePath()
    {
        var mp = Mission();
        return mp is null ? null : Path.Combine(mp.MissionDir, RelativePath);
    }

    /// <summary>Load the file through a pure parser. Returns an empty list when the file is
    /// absent or unresolvable.</summary>
    protected List<T> LoadList<T>(Func<string, List<T>> parse)
    {
        var path = FilePath();
        if (path is null || !File.Exists(path)) return new List<T>();
        try { return parse(File.ReadAllText(path)); }
        catch { return new List<T>(); }
    }

    /// <summary>Raw current file text (or null when absent/unresolvable). Used by the tray's per-tab
    /// undo/redo, which snapshots the whole file before each edit and restores it verbatim.</summary>
    public string? ReadRaw()
    {
        var path = FilePath();
        if (path is null || !File.Exists(path)) return null;
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    /// <summary>Overwrite the file with <paramref name="xml"/> verbatim (snapshots a backup first).
    /// Used by undo/redo. Never throws.</summary>
    public (bool ok, string msg) WriteRaw(string xml)
    {
        var path = FilePath();
        if (path is null) return (false, NoMissionMsg);
        try
        {
            CeBackup.Snapshot(path);
            File.WriteAllText(path, xml);
            return (true, "restored");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Run an in-place edit: parse (or seed when <see cref="SeedRootName"/> allows it),
    /// apply <paramref name="edit"/>, snapshot, write. Never throws.</summary>
    protected (bool ok, string msg) Edit(Func<XDocument, bool> edit, string successMsg, string noOpMsg)
    {
        var path = FilePath();
        if (path is null) return (false, NoMissionMsg);
        if (!File.Exists(path) && SeedRootName is null)
            return (false, $"{Path.GetFileName(RelativePath)} not found in the mission");
        try
        {
            var doc = File.Exists(path)
                ? CeXml.ParseDoc(File.ReadAllText(path))
                : new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(SeedRootName!));

            if (!edit(doc)) return (false, noOpMsg);
            CeBackup.Snapshot(path);
            File.WriteAllText(path, CeXml.Serialize(doc));
            return (true, successMsg);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
