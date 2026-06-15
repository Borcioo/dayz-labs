namespace Dzl.Core.Economy;

/// <summary>One engine-defined <c>db/globals.xml</c> variable: its fixed type, the vanilla default, and a
/// short description. <see cref="IsFloat"/> maps to the file's <c>type</c> attribute (0 = int, 1 = float).</summary>
public sealed record GlobalDef(string Name, bool IsFloat, string Default, string Description)
{
    /// <summary>The <c>type</c> attribute value (0 = int, 1 = float).</summary>
    public int Type => IsFloat ? 1 : 0;
}

/// <summary>
/// The fixed engine vocabulary of <c>db/globals.xml</c> simulation variables. globals.xml is a CLOSED set:
/// the engine reads only these known names (unknown names are ignored), every variable has a hard-coded
/// default, and a missing variable simply falls back to that default — so removing one is "revert to default",
/// not a deletion. Names + types + defaults are the vanilla Chernarus values (which match the documented
/// defaults); descriptions are condensed from the official DayZ Central Economy Configuration wiki.
/// Used by the Globals editor to: tell standard vars from custom ones, offer only missing-known names to add,
/// seed an added var with its default, and reset a tuned var back to default.
/// </summary>
public static class GlobalsCatalog
{
    public static IReadOnlyList<GlobalDef> All { get; } = new[]
    {
        new GlobalDef("AnimalMaxCount", false, "200", "Max spawned animals (non-ambient) across all map zones."),
        new GlobalDef("CleanupAvoidance", false, "100", "Min distance from a player before an item can be deleted (m)."),
        new GlobalDef("CleanupLifetimeDeadAnimal", false, "1200", "Lifetime of dead animals before cleanup (s)."),
        new GlobalDef("CleanupLifetimeDeadInfected", false, "330", "Lifetime of dead infected before cleanup (s)."),
        new GlobalDef("CleanupLifetimeDeadPlayer", false, "3600", "Lifetime of a dead player's body before cleanup (s)."),
        new GlobalDef("CleanupLifetimeDefault", false, "45", "Lifetime for entities with no economy setup but damage ≥ 1.0, i.e. dead (s)."),
        new GlobalDef("CleanupLifetimeLimit", false, "50", "Max items deleted at once during a standard cleanup pass."),
        new GlobalDef("CleanupLifetimeRuined", false, "330", "Lifetime of ruined loot before cleanup (s)."),
        new GlobalDef("FlagRefreshFrequency", false, "432000", "How often a territory flag refreshes nearby item lifetimes (s)."),
        new GlobalDef("FlagRefreshMaxDuration", false, "3456000", "How long a territory flag keeps refreshing item lifetimes (s)."),
        new GlobalDef("FoodDecay", false, "1", "Enable food spoilage / plant lifecycle simulation (0 or 1)."),
        new GlobalDef("IdleModeCountdown", false, "60", "Enter economy idle mode this long after the server empties (s)."),
        new GlobalDef("IdleModeStartup", false, "1", "1 = allow economy idle mode at startup; 0 = disable it at startup."),
        new GlobalDef("InitialSpawn", false, "100", "Loot present on a fresh start, as a percentage of nominal (%)."),
        new GlobalDef("LootDamageMax", true, "0.82", "Maximum damage (0..1) freshly spawned loot may have."),
        new GlobalDef("LootDamageMin", true, "0.0", "Minimum damage (0..1) freshly spawned loot may have."),
        new GlobalDef("LootProxyPlacement", false, "1", "Allow loot to use building proxy placement slots (0 or 1)."),
        new GlobalDef("LootSpawnAvoidance", false, "100", "Min distance from players for new loot to (re)spawn (m)."),
        new GlobalDef("RespawnAttempt", false, "2", "Loot respawn tuning — attempts per economy update cycle."),
        new GlobalDef("RespawnLimit", false, "20", "Loot respawn tuning — max pieces respawned per update cycle."),
        new GlobalDef("RespawnTypes", false, "12", "Loot respawn tuning — max distinct types respawned per update cycle."),
        new GlobalDef("RestartSpawn", false, "0", "Loot spawn behaviour on server restart (0 or 1)."),
        new GlobalDef("SpawnInitial", false, "1200", "Loot pieces spawned per economy update during the initial fill."),
        new GlobalDef("TimeHopping", false, "60", "Cooldown applied to server hopping (s)."),
        new GlobalDef("TimeLogin", false, "15", "Login / connect spawn timer (s)."),
        new GlobalDef("TimeLogout", false, "15", "Logout timer before a disconnect completes (s)."),
        new GlobalDef("TimePenalty", false, "20", "Extra penalty time for unsafe logout / combat logging (s)."),
        new GlobalDef("WorldWetTempUpdate", false, "1", "Enable world wetness / temperature simulation updates (0 or 1)."),
        new GlobalDef("ZombieMaxCount", false, "1000", "Max spawned infected across all map zones."),
        new GlobalDef("ZoneSpawnDist", false, "300", "Distance from players at which dynamic zones (de)spawn (m)."),
    };

    private static readonly Dictionary<string, GlobalDef> ByName =
        All.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The catalog entry for <paramref name="name"/> (case-insensitive), or null if it is not a
    /// known engine variable (i.e. a custom/non-standard key).</summary>
    public static GlobalDef? Find(string name) =>
        name is not null && ByName.TryGetValue(name, out var d) ? d : null;

    /// <summary>True when <paramref name="name"/> is an engine-defined global (not a custom key).</summary>
    public static bool IsKnown(string name) => Find(name) is not null;
}
