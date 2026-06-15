using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active mission's <c>db/economy.xml</c> — the per-entity-group lifecycle switches
/// (init / load / respawn / save) of the Central Economy.
/// </summary>
public sealed class EconomyService : CeFileService
{
    public EconomyService(string configPath) : base(configPath) { }

    protected override string RelativePath => Path.Combine("db", "economy.xml");
    protected override string? SeedRootName => "economy";

    /// <summary>The mission's <c>db/economy.xml</c> path (whether or not it exists yet), or null when no
    /// mission is resolvable.</summary>
    public string? EconomyPath() => FilePath();

    /// <summary>Read all economy groups. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<EconomyGroup> Load() => LoadList(EconomyXml.Parse);

    /// <summary>Toggle one lifecycle flag (init|load|respawn|save) on an existing group.</summary>
    public (bool ok, string msg) SetFlag(string group, string flag, bool value)
    {
        if (string.IsNullOrWhiteSpace(group)) return (false, "group must not be empty");
        return Edit(
            doc => EconomyXml.SetFlag(doc, group, flag, value),
            $"set {group}/{flag} = {(value ? 1 : 0)}",
            $"group '{group}' not found (or invalid flag '{flag}')");
    }

    /// <summary>Upsert a group with all four flags — used to add a standard group or reset one to its defaults.</summary>
    public (bool ok, string msg) SetGroup(string name, bool init, bool load, bool respawn, bool save)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return Edit(
            doc => EconomyXml.SetGroup(doc, name, init, load, respawn, save),
            $"set group '{name}'",
            $"failed to set group '{name}'");
    }

    /// <summary>Remove a group element (the editor only allows this for custom/non-standard groups).</summary>
    public (bool ok, string msg) RemoveGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return Edit(
            doc => EconomyXml.RemoveGroup(doc, name),
            $"removed group '{name}'",
            $"group '{name}' not found");
    }
}
