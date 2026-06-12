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
public sealed class GlobalsService : CeFileService
{
    public GlobalsService(string configPath) : base(configPath) { }

    protected override string RelativePath => Path.Combine("db", "globals.xml");
    protected override string? SeedRootName => "variables";

    /// <summary>The mission's <c>db/globals.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? GlobalsPath() => FilePath();

    /// <summary>Read all globals vars. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<GlobalVar> Load() => LoadList(GlobalsXml.Parse);

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
