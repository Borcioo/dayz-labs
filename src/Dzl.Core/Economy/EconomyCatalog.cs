namespace Dzl.Core.Economy;

/// <summary>One engine-defined <c>db/economy.xml</c> entity group: its friendly label, description and the
/// vanilla default lifecycle flags (used to seed an added group or reset one to default).</summary>
public sealed record EconomyGroupDef(
    string Name, string Display, string Description, bool Init, bool Load, bool Respawn, bool Save);

/// <summary>
/// The fixed engine vocabulary of <c>db/economy.xml</c> entity groups. Like globals.xml, this is a CLOSED set:
/// the engine recognizes only these group names, each has vanilla defaults, and a missing group falls back to
/// them. Drives the Economy-core editor — standard groups can't be deleted (only reset), only a known-missing
/// group can be added, and a custom/non-standard key (rare) stays removable. Defaults are the vanilla Chernarus
/// values; descriptions are condensed from the DayZ Central Economy docs.
/// </summary>
public static class EconomyCatalog
{
    public static IReadOnlyList<EconomyGroupDef> All { get; } = new[]
    {
        new EconomyGroupDef("dynamic", "Dynamic loot", "The main world loot economy.", true, true, true, true),
        new EconomyGroupDef("animals", "Animals", "Wildlife (non-ambient animals).", true, false, true, false),
        new EconomyGroupDef("zombies", "Infected", "Zombies / infected.", true, false, true, false),
        new EconomyGroupDef("vehicles", "Vehicles", "Cars and other driveable vehicles.", true, true, true, true),
        new EconomyGroupDef("randoms", "Randoms", "Random / dynamic-event spawns.", false, false, true, false),
        new EconomyGroupDef("custom", "Custom", "Custom CE-managed spawns.", false, false, false, false),
        new EconomyGroupDef("building", "Building", "Building-attached / fixed economy objects.", true, true, false, true),
        new EconomyGroupDef("player", "Player", "Player characters.", true, true, true, true),
    };

    private static readonly Dictionary<string, EconomyGroupDef> ByName =
        All.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The catalog entry for <paramref name="name"/> (case-insensitive), or null for a custom/non-standard group.</summary>
    public static EconomyGroupDef? Find(string name) =>
        name is not null && ByName.TryGetValue(name, out var d) ? d : null;

    /// <summary>True when <paramref name="name"/> is an engine-defined economy group (not a custom key).</summary>
    public static bool IsKnown(string name) => Find(name) is not null;
}
