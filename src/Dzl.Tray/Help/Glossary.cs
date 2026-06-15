namespace Dzl.Tray.Help;

/// <summary>Plain-English field cheat-sheets shown by the per-tab <see cref="Dzl.Tray.Controls.GlossaryButton"/>.
/// Central-Economy XML is terse and the field meanings are non-obvious (and differ by context — e.g. an event
/// child's min/max is a weight when max=0 but a literal count when max&gt;0), so each editor exposes a glossary
/// for non-expert modders. Definitions are grounded in the DayZ wiki (Central Economy, Ambient Spawner, Server
/// Messages), the community references at dzconfig.com, and the BohemiaInteractive/DayZ-Central-Economy sample
/// mission. English on purpose — the field names the modder edits are English. One static list per tab; each is
/// bound in XAML via <c>{x:Static help:Glossary.Xxx}</c>.</summary>
public static class Glossary
{
    /// <summary>db/events.xml — the Events editor (event-level fields, children, flags).</summary>
    public static IReadOnlyList<GlossaryEntry> Events { get; } =
    [
        new("Event name", "The CE event id (e.g. AnimalRoeDeer, InfectedCity). cfgeventspawns.xml maps this name to the actual coordinates the event spawns at."),
        new("Nominal", "How many map LOCATIONS can host this event (the pool of spawn points) — not a count of spawned entities. nominal=10 means up to 10 places can have a roe-deer herd."),
        new("Min", "Low-water mark. When the active spawn count drops to this, the server starts spawning more."),
        new("Max", "High-water mark. The server stops spawning once this many are active at the same time. So nominal can exceed max — they count different things (locations vs. active spawns)."),
        new("Lifetime", "Seconds a spawn stays on the map before it despawns, assuming no player keeps it alive."),
        new("Restock", "Delay between individual respawns while the event refills back toward nominal/max."),
        new("SafeRadius", "Won't spawn this close (metres) to a player. 0 = it may spawn right on top of a player."),
        new("DistanceRadius", "Minimum distance (metres) between two instances of the same event."),
        new("CleanupRadius", "How far every player must move away before a spawn's despawn timer (Lifetime) starts counting."),
        new("Position", "fixed = use the coordinates from cfgeventspawns.xml. player = spawn relative to players (ambient/dynamic)."),
        new("Limit", "Which min/max actually governs the count: child = the child entries' min/max; parent = the event's own min/max; custom = an external animal-territories file; mixed = a combination of both."),
        new("Active", "Master on/off switch for this event's spawning (1 = on, 0 = off)."),
        new("Deletable", "Whether the CE cleanup is allowed to delete this spawn. 0 = it persists / is managed differently."),
        new("Init Random", "Randomise the initial state at server start (e.g. spread spawns out instead of all at once)."),
        new("Remove Damaged", "Remove the spawn if it becomes damaged (used e.g. for wrecked vehicles)."),
        new("Children", "The classnames this event spawns and how many of each — together they form one spawned group (e.g. a herd: 1 buck + 2-4 does)."),
        new("Child Min / Max", "If Max>0: a literal COUNT range spawned for that type (e.g. 2-4). If Max=0: Min is a percentage spawn WEIGHT — the children's weights conventionally sum to 100, and the engine normalises by their total."),
        new("Loot Min / Max", "How many loot slots get filled on each spawned entity (e.g. items inside a spawned container)."),
    ];

    /// <summary>types.xml — the Types editor (loot item definitions).</summary>
    public static IReadOnlyList<GlossaryEntry> Types { get; } =
    [
        new("Name", "The exact item classname this entry controls (e.g. AKM, Apple), matching the config.cpp class. Must be unique; the editor flags duplicate or empty names."),
        new("Nominal", "Target number of this item the economy keeps spawned across the map at once, refilled over time. 0 = not spawned by the normal map distribution (common for attachments/ammo that spawn via cargo instead)."),
        new("Min", "Restock floor: when the live count drops to this, the economy starts refilling toward Nominal. Should be ≤ Nominal (the editor warns when Min > a non-zero Nominal)."),
        new("QuantMin / QuantMax", "Per-spawn quantity range for items that hold an amount (ammo in a magazine, liquid in a bottle), as a percentage of capacity. -1 = not set, so the item spawns full. QuantMin must not exceed QuantMax."),
        new("Lifetime", "Seconds a spawned instance persists untouched before the CE cleans it up (e.g. 3600 = 1 hour, 7200 = 2 hours; persistent items use much longer)."),
        new("Restock", "Seconds to wait before topping this item back up after its count falls. 0 = refill as soon as the count is below target, no cooldown."),
        new("Cost", "Relative spawn-priority weight (conventionally 0-100, default 100) used when the economy picks what to put in a slot. A weighting against other eligible items, NOT an absolute count."),
        new("Category", "The single loot category gating where this item can spawn (weapons, food, tools, clothes, …). Must be a name registered in cfglimitsdefinition.xml or the editor flags it."),
        new("Usage", "Location tags saying where the item may appear (Military, Police, Town, Farm, Hunting, …). Spawn points filter by these; values must be registered in cfglimitsdefinition.xml to be honoured."),
        new("Value (tiers)", "Map-zone tiers (Tier1-Tier4, Unique) controlling how far into the map / how high-value an area the item appears in. Stored as the XML value element; names must exist in cfglimitsdefinition.xml."),
        new("Tag", "Optional placement refinement (floor, shelves, ground) on top of usage — only takes effect if the map's buildings declare matching tag points. Names must be registered in cfglimitsdefinition.xml."),
        new("Count-in flags", "count_in_cargo / count_in_hoarder / count_in_map / count_in_player decide which existing instances count toward Nominal: inside containers, in stashes/tents, loose on the map, or carried by players. count_in_map defaults on; the others off."),
        new("Crafted", "Marks the type as relating to player-crafted instances, so crafted copies are accounted separately from economy-spawned ones."),
        new("Deloot", "Marks the item as dynamic-event loot — distributed through events (crashes, convoys) rather than the standard nominal map distribution."),
        new("Source (Vanilla / Mod / Custom)", "Editor-only origin label (not part of types.xml) showing whether the entry comes from the base game, a mod's types file, or your own custom file, so you can scope edits and avoid touching vanilla."),
    ];

    /// <summary>cfglimitsdefinition.xml + cfglimitsdefinitionuser.xml — the Dictionaries editor.</summary>
    public static IReadOnlyList<GlossaryEntry> Dictionaries { get; } =
    [
        new("Base dictionaries", "The four master lists of allowed names — Categories, Tags, Usage flags, Value — in cfglimitsdefinition.xml. Any name a type (types.xml) references must exist here first, or the game ignores it."),
        new("Usage flags", "Where an item may spawn (Town, Military, Police, Coast, …). A type's usage entries must match a name from this list."),
        new("Value (tiers)", "Loot-quality tiers (Tier1-Tier4 plus Unique). Higher tiers usually map to more remote/dangerous areas. A type's value entries must match a name here."),
        new("Tags", "Secondary placement filter (floor, shelves, ground) refining where loot is positioned. Optional, and only effective if the map's buildings declare matching tag points."),
        new("Categories", "The high-level item group (weapons, food, clothes, tools, …). Each type normally has one category, whose name must come from this list."),
        new("Add (to dictionary)", "Adds a new name to the selected base list. Custom usage/value/tag/category names MUST live in cfglimitsdefinition.xml to be honoured by the game — adding here is what makes a custom value 'known' so it passes validation and appears in Types autocomplete (storing it only in editor metadata would be invisible to DayZ)."),
        new("Named combos", "Reusable shortcuts in cfglimitsdefinitionuser.xml that bundle several usage or value flags under one name (e.g. TownVillage = Town + Village). Reference the single combo name in types.xml and the engine expands it to its members."),
        new("Combo type: usage vs value", "A combo groups EITHER usage flags (placement, like Town/Military) OR value flags (loot tiers, like Tier1-Tier4). The game supports only these two kinds — there are no combos for tags or categories."),
        new("Members (of a combo)", "The individual base flags a combo expands to. Each member must already exist in the matching base dictionary (a usage combo's members are real Usage flags; a value combo's are real Value entries) or the reference won't resolve."),
        new("Combo name (reference)", "The name you type into types.xml in place of listing each flag; the engine treats it as a valid usage/value reference. The editor folds combo names into the 'known' sets so they pass validation and autocomplete."),
    ];

    /// <summary>cfgspawnabletypes.xml — the Spawnable Types editor.</summary>
    public static IReadOnlyList<GlossaryEntry> SpawnableTypes { get; } =
    [
        new("Spawnable type", "One entry that defines what loot a specific item or vehicle spawns WITH. Its name should match a classname in types.xml; if not, the editor warns it has no economy entry."),
        new("Cargo blocks", "Loot that spawns INSIDE this item (e.g. items inside a backpack or barrel). Each block is either a preset reference (shared loot table) or a custom-chance block with its own item list; a type can have several."),
        new("Attachments blocks", "Items ATTACHED to this one on spawn (e.g. an optic or magazine on a rifle, wheels on a car). Like cargo, each block is a preset reference or a custom-chance block."),
        new("Preset block (From a preset)", "A block pointing at a reusable loot table by name (from cfgrandompresets.xml). Must be of the matching kind — cargo blocks need a cargo preset, attachments blocks an attachments preset — or the editor flags a missing preset."),
        new("Chance block (Custom chance)", "A block that defines its own spawn chance plus an inline item list, instead of referencing a preset. Use it for loot specific to this one type."),
        new("Spawn chance (block)", "Probability 0-1 that the whole block rolls its loot at all (1 = always, 0.5 = half the time). It gates the block before any individual item chances are considered."),
        new("Item name", "Classname of one item inside a custom-chance block (e.g. Nail, AKM_Mag). The editor does not validate inline classnames against a class database, so a typo silently fails to spawn in-game."),
        new("Chance (item)", "Probability 0-1 that this item is picked when its block rolls. Within one block, items are weighted relative to each other — an item's chance is its share of the draw, not a strict independent percentage."),
        new("Hoarder / Hoard", "When on, this type's loot counts toward the hoarder (stash / buried-base) limit instead of the normal map economy. Shown as the 'Hoard' checkbox column in the list."),
        new("Damage (min / max)", "Spawn-health range 0-1 (0 = pristine, 1 = ruined) — how worn the item spawns. Leave BOTH empty to remove the damage element; keep min ≤ max."),
    ];

    /// <summary>cfgrandompresets.xml — the Random Presets editor.</summary>
    public static IReadOnlyList<GlossaryEntry> RandomPresets { get; } =
    [
        new("Random preset", "A named cargo or attachment 'loot bundle' that cfgspawnabletypes.xml points at by name. Editing one changes loot for every spawnable type that references it. Presets do nothing until referenced."),
        new("Kind (cargo / attachments)", "Whether the preset fills a container's inside (cargo, e.g. ammo in a backpack) or slots onto the item (attachments, e.g. a scope on a rifle). A spawnabletype's cargo reference can only use a cargo preset, attachments only an attachments preset."),
        new("Name (preset name)", "The unique id a spawnabletype references (e.g. preset='militaryHelmets'). Case-insensitive and unique within a kind."),
        new("Chance (preset-level)", "Probability 0-1 that the whole preset is rolled when a spawnabletype uses it (1 = always). If the roll fails, none of its items are applied to that spawn."),
        new("Item name", "Classname of a single item the preset can place (autocompleted over types.xml; free text allowed). A misspelled/non-existent classname silently fails to spawn — cross-check against types.xml."),
        new("Chance (item-level)", "Probability 0-1 that this item is included when the parent preset rolls. Item chances within a preset are commonly a weighted pick, so they are normally meant to sum to about 1.0 across the list rather than each being independent."),
        new("Enable / Disable", "Disabling comments the preset out in the XML rather than deleting it — kept on disk, re-enableable later, but never loaded while disabled. Disabled rows show struck-through."),
        new("Disable unused", "Comments out every preset that no spawnabletype references, in one pass. De-clutters the live set without deleting anything (re-enable per row)."),
        new("Edit preset (rename / change kind)", "Renames the preset and/or switches it between cargo and attachments in place. Does NOT update the spawnabletypes that referenced the old name/kind — fix those separately or they break."),
        new("Unused preset", "A preset referenced by no spawnabletype. Legal but dead weight; flagged as info so you can disable or remove it."),
    ];

    /// <summary>db/globals.xml — the Globals editor (closed engine vocabulary).</summary>
    public static IReadOnlyList<GlossaryEntry> Globals { get; } =
    [
        new("Globals", "Engine settings that tune the Central Economy and world simulation. A closed set of ~30 known names — the engine reads only these and ignores anything else; a missing one simply falls back to its default."),
        new("Type (int / float)", "How the value is written: int (type 0) = whole number, float (type 1) = decimal. Fixed for standard variables; only LootDamageMin/Max are float."),
        new("Reset to default", "Writes the vanilla default back. Because the file is a closed set, a missing variable falls back to its default — so removing a standard var is effectively 'revert to default', not data loss."),
        new("ZombieMaxCount / AnimalMaxCount", "Hard caps on infected (default 1000) and non-ambient animals (default 200) alive across the whole map at once. Raising them increases server load."),
        new("ZoneSpawnDist", "Distance from players (metres, default 300) at which dynamic infected/animal zones spawn in and despawn out."),
        new("CleanupLifetime*", "Seconds dead/ruined entities persist before the cleanup pass deletes them (dead player body 3600, ruined loot 330, …). CleanupLifetimeDefault (45) applies to dead entities with no economy setup."),
        new("CleanupAvoidance", "Minimum distance (metres, default 100) a player must be before an item can be deleted, so loot isn't culled in front of players. CleanupLifetimeLimit (50) caps how many items are removed per cleanup pass."),
        new("InitialSpawn / SpawnInitial", "InitialSpawn = loot present on a fresh start, as a percentage of nominal (100 = full). SpawnInitial (1200) = how many pieces are spawned per economy update during that initial fill."),
        new("Respawn tuning", "RespawnAttempt (2), RespawnLimit (20) and RespawnTypes (12) throttle how aggressively loot replenishes each economy cycle: attempts per cycle, max pieces, and max distinct item types."),
        new("LootDamageMin / LootDamageMax", "Damage range (0.0-1.0) freshly spawned loot may carry (0 = pristine, 1 = ruined). Defaults 0.0 / 0.82 — vanilla loot can spawn worn but never ruined."),
        new("Connection timers", "Seconds: TimeLogin (spawn-in delay, 15), TimeLogout (logout countdown, 15), TimePenalty (combat-log penalty, 20), TimeHopping (server-hop cooldown, 60)."),
        new("Idle mode", "Lets an empty server sleep the simulation to save CPU. IdleModeCountdown (60s after the last player leaves) before idling; IdleModeStartup (0/1) allows idle at startup."),
        new("FoodDecay / WorldWetTempUpdate", "0/1 toggles: FoodDecay (1) enables food spoilage and the plant lifecycle; WorldWetTempUpdate (1) enables world wetness/temperature updates. Off disables those systems server-wide."),
    ];

    /// <summary>db/economy.xml — the Economy core editor (per-group lifecycle board).</summary>
    public static IReadOnlyList<GlossaryEntry> EconomyCore { get; } =
    [
        new("Economy core (db/economy.xml)", "The master switch board controlling which lifecycle phases the CE runs for each entity group. Each row is a group; the four toggles turn its init/load/respawn/save behaviour on or off."),
        new("Group", "An entity group the engine processes separately (dynamic, animals, zombies, vehicles, randoms, custom, building, player). A closed engine vocabulary — only these names are recognised; a missing group falls back to built-in defaults."),
        new("Init", "When on, the CE CREATES this group on server startup (init=1). For loot (dynamic) this is what populates the world on a fresh start."),
        new("Load", "When on, the group is RESTORED from persistence at startup, so saved state (parked vehicles, stored items) survives a restart (load=1)."),
        new("Respawn", "When on, the engine REPLENISHES this group at runtime while the server is live — loot trickling back, animals/infected reappearing (respawn=1)."),
        new("Save", "When on, the engine PERSISTS this group to storage so its state survives a restart (save=1). Save off but Load on means it loads old data but never writes new data back."),
        new("dynamic (loot)", "The main world loot economy. Vanilla: all four phases on — loot must spawn, persist, and replenish."),
        new("animals / zombies", "Wildlife and infected. Vanilla: init + respawn on, load + save off — created and respawning at runtime, but not persisted across restarts."),
        new("vehicles", "Cars and driveables. Vanilla: all four on, so spawned/parked vehicles persist and are restored after a restart (turning Save/Load off would lose player-parked vehicles)."),
        new("randoms / custom / building / player", "randoms: respawn only. custom: all off (a slot for mod-managed objects). building: init+load+save on, respawn off. player: all on — characters are created, loaded and saved (don't disable, or player progress won't persist)."),
        new("Reset (standard group)", "Restores a standard group's four flags to vanilla defaults. Standard groups can't be deleted — only reset — because the engine always expects them."),
    ];

    /// <summary>cfgeconomycore.xml — the CE Config editor (routing manifest + defaults + root classes).</summary>
    public static IReadOnlyList<GlossaryEntry> CeCore { get; } =
    [
        new("Custom CE files (routing)", "The manifest in cfgeconomycore.xml that registers which custom CE files (types, spawnabletypes, events, …) to load from your mission. A file you add in another tab does NOTHING until it's registered here."),
        new("folder", "The mission subfolder a registered custom file lives in, relative to the mission root (e.g. ce/MyMod). Files sharing a folder are grouped under one ce-folder block."),
        new("file (name)", "Exact filename of the custom CE file to load, including .xml (e.g. mymod_types.xml). The folder + file pair must be unique."),
        new("type (CE file type)", "What kind of CE data the registered file holds, so the engine reads it correctly. Allowed: types, spawnabletypes, globals, economy, events, messages."),
        new("Defaults", "Engine tuning knobs in the defaults section — dynamic infected zone sizing, CE logging toggles, startup/persistence options. Semi-open: missions may carry a subset or extras (extras appear under 'Other')."),
        new("dyn_radius / dyn_smin..dmax", "Default sizing for dynamic infected (zombie) zones when a zone doesn't override it: dyn_radius = zone radius in metres (20); smin/smax bound static infected (0/0); dmin/dmax bound dynamic infected (0/5)."),
        new("log_ce_* toggles", "Write detailed CE diagnostics to the logs (loop, dynamicevent, vehicle, lootspawn, lootcleanup, lootrespawn, statistics, zombie). All default OFF — enable one only while debugging; they make logs very noisy."),
        new("log_hivewarning / log_missionfilewarning", "Warning toggles, both ON by default: warn on hive (database) issues and on malformed mission CE files. Leave on to catch broken configs early."),
        new("world_segments", "Splits the world into N segments for CE processing (default 12). A performance knob for large maps; the default is fine for most missions."),
        new("backup_period / backup_count", "Persistence backups: how often a backup runs (minutes, default 60) and how many backup folders to keep before the oldest is recycled (default 12). backup_startup runs one at boot."),
        new("Root classes", "The base classes the CE manages (loot / character / car), shown read-only because changing them is advanced and risky. act binds a class to a behaviour (character or car); loot classes usually have no act."),
    ];

    /// <summary>cfgignorelist.xml — the Ignore list editor.</summary>
    public static IReadOnlyList<GlossaryEntry> IgnoreList { get; } =
    [
        new("Ignore list (cfgignorelist.xml)", "A flat list of item classnames the Central Economy is told to leave alone. Anything listed is exempt from CE spawning, counting, and cleanup."),
        new("classname", "The exact in-game type identifier of an item (e.g. Bandage, AKM, TunaCan), matched literally by the engine. Stored as the name attribute of a type element."),
        new("What 'ignore' does", "Removes the listed types from CE oversight — the engine never spawns, tracks, or cleans them up. Typically used for items placed/owned outside the economy (modded or event-placed) so the cleanup pass doesn't delete them."),
        new("classname format", "Only a bare identifier is accepted: no spaces and none of the XML-reserved characters < > & ' \" — because the name is written into an XML attribute and matched literally."),
        new("Duplicate rejection", "Adding a classname already in the list is blocked (case-insensitive); you get a status like 'X is already ignored' instead of a second entry."),
    ];

    /// <summary>cfgeventspawns.xml — the Event Spawns editor (per-event coordinates).</summary>
    public static IReadOnlyList<GlossaryEntry> EventSpawns { get; } =
    [
        new("Event Spawns (cfgeventspawns.xml)", "The fixed map positions where each named event can spawn — a list of events, each with a list of X/Z/angle points. This file supplies WHERE; events.xml supplies the timing and amount."),
        new("Event", "The name of one event (left list). Must exactly match an event defined in events.xml. The economy picks from this event's positions (subject to its own rules/cooldowns) to place it in the world."),
        new("Positions", "The candidate spawn points for the selected event, one row per pos entry. At spawn time the game picks from this list."),
        new("X", "World east-west coordinate, in metres from the map origin. On standard maps roughly 0-15360 (Chernarus/Livonia are 15.36 km wide)."),
        new("Z", "World north-south coordinate, in metres from the map origin. DayZ uses X and Z for the ground plane (not X/Y), so X/Z together is your top-down location."),
        new("Angle", "Yaw (compass heading) the spawned object faces, in degrees, stored as the 'a' attribute. 0 = facing north; left at 0 where orientation doesn't matter."),
        new("Height (Y)", "Normally omitted so the game snaps the object to the terrain surface. This editor writes X/Z/A only; an existing y attribute in the file is preserved untouched on save, not dropped."),
    ];

    /// <summary>cfgeventgroups.xml — the Event Groups editor.</summary>
    public static IReadOnlyList<GlossaryEntry> EventGroups { get; } =
    [
        new("Event group", "A named bundle of objects an event spawns together as one unit (one group in cfgeventgroups.xml). Events reference a group by name instead of listing objects individually, so the whole set spawns and despawns as a package."),
        new("Group (name)", "The unique name an event spawn points at (case-insensitive here). Renaming a group means events that referenced the old name no longer find it."),
        new("Objects", "The child objects of the selected group. Each row is one object placed relative to the group's spawn point."),
        new("Type", "The classname of the object to spawn for this row (e.g. a wreck, vehicle, or building). Must be a valid spawnable classname or it won't appear."),
        new("X / Y / Z", "Offset of this object from the group's origin, in metres (relative, not absolute map coordinates), so the whole group can be placed while objects keep their layout. Y is height — usually 0 to sit on terrain."),
        new("Angle", "The object's yaw rotation in degrees (the 'a' attribute) relative to the group; 0 = no rotation."),
        new("Loot min / max", "Random range of loot items to seed this object with on spawn (lootmin/lootmax attributes). Only matters for objects that act as loot containers; set both to 0 for none, keep min ≤ max."),
        new("Deloot", "When on (1), the object spawns with dynamic/event loot attached rather than empty. Vanilla wrecks commonly set this to get their cargo."),
    ];

    /// <summary>cfgplayerspawnpoints.xml — the Player Spawns editor.</summary>
    public static IReadOnlyList<GlossaryEntry> PlayerSpawns { get; } =
    [
        new("Category (Fresh / Hop / Travel)", "The spawn situation being configured: Fresh = brand-new characters (required); Hop = re-joining the same map; Travel = moving between maps. Hop/Travel apply mostly to official servers and are usually ignored on community servers."),
        new("Spawn scoring (spawn_params)", "Ranks candidate spawn points at spawn time by distance to nearby things. A point inside the min distance is avoided, scores best between min and max, then the bonus fades beyond max — more clear distance = a more preferred spawn."),
        new("Infected / Players / Buildings (min-max)", "min/max distance (metres) a spawn point should keep from zombies (infected), other players, and static objects (buildings). The editor weights player distance most heavily, then infected, then buildings, when ranking points."),
        new("Grid generation (generator_params)", "How candidate points are produced around each spawn position: a width × height rectangle is built around the position and sampled on a grid to find valid standing spots."),
        new("Density", "How many sample points are taken per side of the generation rectangle. Higher = more (and finer) candidate points, at more computation."),
        new("Size (width × height)", "Dimensions (metres) of the rectangle generated around each spawn position. Larger spreads candidate points over a wider area so players don't all land on one coordinate."),
        new("Steepness (min-max)", "Allowed terrain slope (degrees) for a generated point. Points steeper than max (or flatter than min) are rejected so characters don't spawn on cliffs."),
        new("Group cycling (group_params)", "When enabled, players spawn within one currently-active position group; after its lifetime expires the active group rotates. Off treats every point as one flat pool."),
        new("Enable groups", "Turns the group-cycling system on. On = only the active group is used at a given moment; off = all groups' points are one combined list with no rotation."),
        new("Lifetime / Counter", "Lifetime = seconds the active group stays active before rotating. Counter = how many players spawning in it reset/extend its lifetime (-1 disables). Both only matter when Enable groups is on."),
        new("Container (posbubbles vs permanent)", "Chosen when adding a group: generator_posbubbles = each position is the centre of a generated grid of candidate points (normal); permanent = the listed coordinates are exact fixed spawn points with no grid sampling."),
        new("Position (X / Z)", "A single spawn point as world X and Z map coordinates (height comes from the terrain). For posbubbles groups it's the centre of a bubble; for permanent groups it's an exact spot."),
        new("Other params", "Any non-standard keys found in a category's params that this editor doesn't recognise. Shown so they can be edited inline and preserved exactly on save, never silently dropped."),
    ];

    /// <summary>cfgweather.xml — the Weather editor.</summary>
    public static IReadOnlyList<GlossaryEntry> Weather { get; } =
    [
        new("Enable", "Master on/off (enable=1, default on) for this weather config. When off, the values here are ignored and the server uses engine defaults."),
        new("Reset", "When set (reset=1), the server ignores weather saved in storage and restarts from the values here. Leave off to let weather persist; turn on once after big edits to force them to take effect."),
        new("Channel", "Each card is one weather channel simulated independently: overcast, fog, rain, wind, snowfall, storm. Fields inside a card affect only that channel."),
        new("overcast", "Cloud cover, 0.0-1.0 (0 = clear, 1 = fully overcast). The parent driver: rain and fog generally only build once overcast is high enough."),
        new("fog / rain / snowfall", "Intensity 0.0-1.0 (0 = none, 1 = heaviest). Rain typically only starts once overcast crosses a threshold; visible snowfall usually needs a map/mod that supports snow."),
        new("wind", "Wind strength 0.0-1.0, driving the wind force used for sound, particles and weather drift."),
        new("storm", "Thunderstorm channel. Unlike the others its settings are density, threshold (the overcast level that must be reached before a storm starts) and timeout (cooldown between storms)."),
        new("current · actual", "The channel's current/starting value (0.0-1.0). Weather begins here; the engine then drifts it over time toward new randomly chosen targets within the limits."),
        new("current · time / duration", "time = seconds until the channel next picks a new target (how soon weather starts changing); duration = seconds the transition itself takes (larger = smoother, slower change)."),
        new("limits · min / max", "Lowest and highest values (0.0-1.0) the channel may randomly drift between. Set min = max to pin a channel (e.g. overcast 0/0 keeps skies permanently clear)."),
        new("timelimits · min / max", "Shortest and longest time (seconds) the engine waits before the channel picks a new target — bounds how frequently this weather type changes."),
    ];

    /// <summary>cfgenvironment.xml + env/*_territories.xml — the Environment editor.</summary>
    public static IReadOnlyList<GlossaryEntry> Environment { get; } =
    [
        new("cfgenvironment.xml", "Registers the per-zone territory files and defines the animal/infected 'territories' (herds and ambient groups) the CE spawns into the world."),
        new("Territory", "One named animal or infected group (e.g. AmbientHen). Selecting it loads its tunable knobs and spawn list."),
        new("Type / Behavior", "Type = the category of the territory (e.g. Ambient). Behavior = the script class driving how members move/group/patrol (e.g. DZAmbientLifeGroupBeh) — not normally changed unless you know the matching class."),
        new("globalCountMax", "Maximum agents of this territory alive across the whole map at once. The engine stops spawning new ones once the cap is hit; raising it means more of that animal/infected world-wide."),
        new("zoneCountMax / zoneCountMin", "Per-zone caps: zoneCountMax limits how many agents one zone holds at a time (caps local density); zoneCountMin is the floor the engine tops a zone up toward."),
        new("Spawns (read-only)", "Which actual classes this territory spawns and at what chance. Display-only here — to add/remove classes or change odds, edit cfgenvironment.xml directly."),
        new("Agent", "The role/sex bucket a spawn belongs to within the territory (e.g. Male/Female). Each agent group holds one or more spawnable classes with its own selection chance."),
        new("configName", "The exact game class spawned for an entry (e.g. Animal_GallusGallusDomesticus). Must match a valid entity classname or nothing spawns."),
        new("Chance", "Probability (typically 0-1) that this class is chosen when its agent group spawns a member. 1 = always picked; lower values share the slot with other classes."),
        new("Territory files", "The env/*_territories.xml files registered at the top of cfgenvironment.xml. They hold the actual map zone geometry (where each herd can roam) and are edited externally — use the VS Code / Reveal buttons."),
    ];

    /// <summary>db/messages.xml — the Messages editor (server broadcast scheduler; NOT a CE file).</summary>
    public static IReadOnlyList<GlossaryEntry> Messages { get; } =
    [
        new("Messages (db/messages.xml)", "The server message scheduler — not a Central Economy file. It drives periodic broadcasts, an on-connect welcome line, and scheduled restart/shutdown countdowns. Each row is one independent message the server fires on its own timer."),
        new("message text", "The line broadcast to all connected players on this message's schedule. May embed placeholders the server fills in at send time (see #name, #tmin)."),
        new("delay", "How long after server start before the message first shows. The in-app hint labels this in minutes; confirm the unit against your server build, as community references differ on minutes vs. seconds."),
        new("repeat", "How often the message shows again after its first appearance; 0 = show only once. Same unit as delay."),
        new("deadline", "When > 0, the message acts as a timed countdown toward an event (typically a restart), used together with shutdown to schedule a server stop."),
        new("on connect", "Stored as onConnect (1/0). When on, the message is shown to each player the moment they join — a per-player welcome rather than a timed broadcast."),
        new("shutdown", "Stored as shutdown (1/0). When on, the server stops itself once this message's countdown ends — used to script an automatic restart. Off for ordinary informational broadcasts."),
        new("#name (placeholder)", "Token in the message text the server replaces with the server's name (e.g. '#name restarts soon')."),
        new("#tmin (placeholder)", "Token the server replaces with the minutes remaining, for restart/shutdown countdown lines (e.g. '#name restarts in #tmin min')."),
    ];

    /// <summary>mapgroup*/mapcluster* — the Map files tab (shortcuts to auto-generated terrain data).</summary>
    public static IReadOnlyList<GlossaryEntry> MapFiles { get; } =
    [
        new("Map files", "Auto-generated terrain object-data files (map*.xml) in the active mission. This tab only LOCATES and opens them externally — there's no in-app editor, because the game writes them via an in-game export, not by hand."),
        new("Auto-generated (export-only)", "All map*.xml files are produced by an in-game export (the CE proxy/cluster data dump), not authored by hand. Editing raw coordinates manually is error-prone and can break spawning — hence open/reveal only, no editor."),
        new("mapgrouppos.xml", "Building (group) instance positions on the terrain — where each loot-spawning structure physically sits on the map. Exported in-game."),
        new("mapgroupproto.xml", "Loot-point prototype definitions per building type: for each kind of structure, the named loot positions (slots) and their attributes (loot category, placement flags, usage tags) the CE uses to place items."),
        new("mapgroupcluster.xml", "Cluster spawn data for harvestable environment objects (fruit trees, bushes, sticks, stones) — where those clusters appear on the map."),
        new("mapclusterproto.xml", "Cluster prototype definitions: templates describing what each cluster type contains and how it behaves (e.g. which items a fruit-tree or stone cluster yields)."),
        new("mapgroupdirt.xml", "Dirt/ground-level cluster spawn data exported by the game. Like the other map* files, treat it as inspect-only unless you know exactly what a coordinate change does."),
        new("VS Code / Reveal", "VS Code opens the selected file in Visual Studio Code (or the OS default .xml editor) — these files can be huge, so loading may be slow. Reveal opens Explorer with the file selected for copy/backup."),
    ];
}
