using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active server instance's mission <c>db/globals.xml</c> — the flat list of
/// simulation variables (animal counts, loot damage limits, etc.).
/// Mirrors the <see cref="RandomPresetsService"/> pattern: one facade per frontend, never throws (returns
/// ok+message), snapshots a backup (<see cref="CeBackup"/>) before every write, edits in place so
/// comments/order survive a round-trip (<see cref="GlobalsXml"/>).
/// </summary>
public sealed class GlobalsService
{
    private readonly string _configPath;

    public GlobalsService(string configPath) { _configPath = configPath; }

    // ------------------------------------------------------------------
    // Path resolution
    // ------------------------------------------------------------------

    private MissionPaths? Mission()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return MissionLocator.Resolve(cfg);
    }

    /// <summary>The mission's <c>db/globals.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? GlobalsPath()
    {
        var mp = Mission();
        return mp is null ? null : Path.Combine(mp.Db, "globals.xml");
    }

    // ------------------------------------------------------------------
    // Read
    // ------------------------------------------------------------------

    /// <summary>Read all globals vars. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<GlobalVar> Load()
    {
        var path = GlobalsPath();
        if (path is null || !File.Exists(path)) return new List<GlobalVar>();
        try { return GlobalsXml.Parse(File.ReadAllText(path)); }
        catch { return new List<GlobalVar>(); }
    }

    /// <summary>Raw current file text (or null when absent/unresolvable). Used by the tray's per-tab
    /// undo/redo, which snapshots the whole file before each edit and restores it verbatim.</summary>
    public string? ReadRaw()
    {
        var path = GlobalsPath();
        if (path is null || !File.Exists(path)) return null;
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    /// <summary>Overwrite the file with <paramref name="xml"/> verbatim (snapshots a backup first).
    /// Used by undo/redo. Never throws.</summary>
    public (bool ok, string msg) WriteRaw(string xml)
    {
        var path = GlobalsPath();
        if (path is null) return (false, "no mission resolved for the active server");
        try
        {
            CeBackup.Snapshot(path);
            File.WriteAllText(path, xml);
            return (true, "restored");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ------------------------------------------------------------------
    // Write helper
    // ------------------------------------------------------------------

    private (bool ok, string msg) Edit(Func<XDocument, bool> edit, string successMsg, string noOpMsg)
    {
        var path = GlobalsPath();
        if (path is null) return (false, "no mission resolved for the active server");
        try
        {
            var doc = File.Exists(path)
                ? GlobalsXml.ParseDoc(File.ReadAllText(path))
                : new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("variables"));

            if (!edit(doc)) return (false, noOpMsg);
            CeBackup.Snapshot(path);
            File.WriteAllText(path, GlobalsXml.ToXml(doc));
            return (true, successMsg);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ------------------------------------------------------------------
    // Var-level edits
    // ------------------------------------------------------------------

    /// <summary>Upsert (add or update) a var. Returns ok=true with a message describing what happened.</summary>
    public (bool ok, string msg) SetVar(string name, int type, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "var name must not be empty");
        return Edit(
            doc => GlobalsXml.SetVar(doc, name, type, value),
            $"set var '{name}'",
            $"failed to set var '{name}'");
    }

    /// <summary>Remove a var by name.</summary>
    public (bool ok, string msg) RemoveVar(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "var name must not be empty");
        return Edit(
            doc => GlobalsXml.RemoveVar(doc, name),
            $"removed var '{name}'",
            $"var '{name}' not found");
    }

    /// <summary>Rename a var (preserves position/type/value).</summary>
    public (bool ok, string msg) RenameVar(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return (false, "old name must not be empty");
        if (string.IsNullOrWhiteSpace(newName)) return (false, "new name must not be empty");
        return Edit(
            doc => GlobalsXml.RenameVar(doc, oldName, newName),
            $"renamed var '{oldName}' → '{newName}'",
            $"rename failed: '{oldName}' not found or '{newName}' already exists");
    }
}
