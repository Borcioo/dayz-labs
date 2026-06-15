namespace Dzl.Core.Economy;

/// <summary>One documented cfgeconomycore <c>&lt;default&gt;</c> knob: its group, whether it's a boolean, the
/// engine default, and a short description.</summary>
public sealed record CeDefaultDef(string Name, string Group, bool IsBool, string Default, string Description);

/// <summary>
/// The documented cfgeconomycore default knobs, grouped for a friendly editor (dynamic infected zones,
/// CE logging, startup &amp; persistence). Values/defaults from the DayZ Central Economy Configuration wiki +
/// vanilla. Unlike globals this set is semi-open (missions may carry a subset or extras) — unknown defaults
/// surface under "Other".
/// </summary>
public static class CeCoreDefaults
{
    public const string Zones = "Dynamic infected zones";
    public const string Logging = "CE logging";
    public const string Startup = "Startup & persistence";

    public static IReadOnlyList<CeDefaultDef> All { get; } = new[]
    {
        new CeDefaultDef("dyn_radius", Zones, false, "20", "Default dynamic infected zone radius (m)."),
        new CeDefaultDef("dyn_smin", Zones, false, "0", "Default zone minimal static infected count."),
        new CeDefaultDef("dyn_smax", Zones, false, "0", "Default zone maximal static infected count."),
        new CeDefaultDef("dyn_dmin", Zones, false, "0", "Default zone minimal dynamic infected count."),
        new CeDefaultDef("dyn_dmax", Zones, false, "5", "Default zone maximal dynamic infected count."),

        new CeDefaultDef("log_ce_loop", Logging, true, "false", "Log the CE main loop."),
        new CeDefaultDef("log_ce_dynamicevent", Logging, true, "false", "Log dynamic events."),
        new CeDefaultDef("log_ce_vehicle", Logging, true, "false", "Log vehicle CE."),
        new CeDefaultDef("log_ce_lootspawn", Logging, true, "false", "Log loot spawning."),
        new CeDefaultDef("log_ce_lootcleanup", Logging, true, "false", "Log loot cleanup."),
        new CeDefaultDef("log_ce_lootrespawn", Logging, true, "false", "Log loot respawn."),
        new CeDefaultDef("log_ce_statistics", Logging, true, "false", "Log CE statistics."),
        new CeDefaultDef("log_ce_zombie", Logging, true, "false", "Log infected CE."),
        new CeDefaultDef("log_storageinfo", Logging, true, "false", "Log persistence storage info."),
        new CeDefaultDef("log_hivewarning", Logging, true, "true", "Warn on hive issues."),
        new CeDefaultDef("log_missionfilewarning", Logging, true, "true", "Warn on malformed mission CE files."),

        new CeDefaultDef("world_segments", Startup, false, "12", "World split into N segments for CE processing (perf for big maps)."),
        new CeDefaultDef("backup_period", Startup, false, "60", "Persistence backup period (minutes)."),
        new CeDefaultDef("backup_count", Startup, false, "12", "Persistence backup folders to keep."),
        new CeDefaultDef("backup_startup", Startup, true, "false", "Run a persistence backup at server startup."),
        new CeDefaultDef("save_events_startup", Startup, true, "false", "Persist events on startup."),
        new CeDefaultDef("save_types_startup", Startup, true, "false", "Persist types on startup."),
    };

    private static readonly Dictionary<string, CeDefaultDef> ByName =
        All.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    public static CeDefaultDef? Find(string name) =>
        name is not null && ByName.TryGetValue(name, out var d) ? d : null;
}
