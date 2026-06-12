using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active server instance's mission <c>cfgplayerspawnpoints.xml</c> — the hierarchical
/// player-spawn config (fresh/hop/travel categories, their param bags, and named position-group bubbles).
/// Mirrors the <see cref="RandomPresetsService"/> pattern: one facade per frontend, never throws (returns
/// ok+message), snapshots a backup (<see cref="CeBackup"/>) before every write, edits in place so
/// comments/order survive a round-trip (<see cref="PlayerSpawnsXml"/>).
/// </summary>
public sealed class PlayerSpawnsService : CeFileService
{
    public PlayerSpawnsService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgplayerspawnpoints.xml";
    protected override string? SeedRootName => null;   // edits require an existing file

    /// <summary>The mission's <c>cfgplayerspawnpoints.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? SpawnsPath() => FilePath();

    /// <summary>Read all spawn categories. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<SpawnCategory> Load() => LoadList(PlayerSpawnsXml.Parse);

    // ------------------------------------------------------------------
    // Param edits
    // ------------------------------------------------------------------

    /// <summary>Upsert a scalar param in a category's section (spawn_params|generator_params|group_params).</summary>
    public (bool ok, string msg) SetParam(string category, string section, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(category)) return (false, "category must not be empty");
        if (string.IsNullOrWhiteSpace(name)) return (false, "param name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.SetParam(doc, category, section, name, value),
            $"set {category}/{section}/{name}",
            $"category '{category}' not found");
    }

    // ------------------------------------------------------------------
    // Group edits
    // ------------------------------------------------------------------

    /// <summary>Add a named group to a category's bubbles container.</summary>
    public (bool ok, string msg) AddGroup(string category, string container, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return (false, "group name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.AddGroup(doc, category, container, groupName),
            $"added group '{groupName}' to {category}/{container}",
            $"group '{groupName}' not added (category missing or group already present)");
    }

    /// <summary>Remove a named group from a category's bubbles container.</summary>
    public (bool ok, string msg) RemoveGroup(string category, string container, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return (false, "group name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.RemoveGroup(doc, category, container, groupName),
            $"removed group '{groupName}' from {category}/{container}",
            $"group '{groupName}' not found in {category}/{container}");
    }

    /// <summary>Rename a group within a category's bubbles container.</summary>
    public (bool ok, string msg) RenameGroup(string category, string container, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return (false, "old name must not be empty");
        if (string.IsNullOrWhiteSpace(newName)) return (false, "new name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.RenameGroup(doc, category, container, oldName, newName),
            $"renamed group '{oldName}' → '{newName}'",
            $"rename failed: '{oldName}' not found or '{newName}' already exists");
    }

    // ------------------------------------------------------------------
    // Position edits
    // ------------------------------------------------------------------

    /// <summary>Append a position to a group.</summary>
    public (bool ok, string msg) AddPos(string category, string container, string groupName, double x, double z)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return (false, "group name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.AddPos(doc, category, container, groupName, x, z),
            $"added pos to '{groupName}'",
            $"group '{groupName}' not found in {category}/{container}");
    }

    /// <summary>Remove the position at <paramref name="index"/> from a group.</summary>
    public (bool ok, string msg) RemovePos(string category, string container, string groupName, int index)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return (false, "group name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.RemovePos(doc, category, container, groupName, index),
            $"removed pos #{index} from '{groupName}'",
            $"pos #{index} not removed (group missing or index out of range)");
    }

    /// <summary>Set the x/z of the position at <paramref name="index"/> in a group.</summary>
    public (bool ok, string msg) SetPos(string category, string container, string groupName, int index, double x, double z)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return (false, "group name must not be empty");
        return Edit(
            doc => PlayerSpawnsXml.SetPos(doc, category, container, groupName, index, x, z),
            $"updated pos #{index} in '{groupName}'",
            $"pos #{index} not updated (group missing or index out of range)");
    }
}
