# Phase: Mission Config — modular editors

Goal: edit **every** DayZ mission config file (XML + cfg JSON) from the tray, organised into logical
**modules** (not everything is "economy"), each module a dashboard-style surface with per-file validation.
Auto-generated terrain data is NOT hand-edited — surface it as **shortcuts** (open folder / VS Code / reveal).
Grounded in the DayZ wiki (Central Economy Configuration, Player Spawning, Server Messages, …).

## Information architecture

Left-rail nav group **MISSION CONFIG** → one content panel per module; inside a module, a dashboard tab strip
of its file editors (reuse the established CE tab + nav-rail dashboard patterns + the shared `DarkDataGrid`
selection, NumberBox/ToggleSwitch inputs, Info tooltips, closed-set Add/reset where the file is a fixed vocabulary).

| Module | Files | Notes |
|---|---|---|
| **Economy** (loot) | types.xml, cfgspawnabletypes, cfgrandompresets, cfglimitsdefinition(+user), db/globals, db/economy, **cfgeconomycore (routing)**, **cfgignorelist** + CE Dashboard | existing tabs + 2 new |
| **Events** | db/events, **cfgeventspawns** (per-event positions), **cfgeventgroups** (object groups) | Events moves here; 2 new |
| **World** | **cfgplayerspawnpoints** (moved from Economy — not loot), **cfgenvironment + env/\*_territories**, **cfgweather** | 1 moved, 2 new |
| **Server** | **db/messages** (broadcast/restart scheduler), cfggameplay.json *(later)* | hand-edited but not CE |
| **Map files** | mapgrouppos / mapgroupcluster* / mapgroupdirt / mapgroupproto / mapclusterproto | shortcuts only — open folder / VS Code / reveal; NO in-app editor |

File→module rationale comes from the wiki: messages.xml = server scheduler (not CE); cfgplayerspawnpoints =
world spawns (not loot); cfgenvironment/env = animal/infected territories; map* = exported terrain data.

## Per-file editor model (validation)

- **Closed-vocabulary files** (fixed engine set): standard entries not deletable → reset-to-default, Add only
  known-missing, type/name fixed. Already applied to Globals + Economy core; same model for cfgeconomycore
  routing toggles and any other engine-fixed list.
- **Open lists** (admin-authored): full add/rename/remove with identifier + numeric validation (cfgignorelist
  classnames via the shared AutoSuggest; event spawns positions like Player Spawns; event groups nested).
- **Territory/zone geometry** (env/*, mapgroupproto) is coordinate data → read-mostly / import, not free hand-edit.
- Every editor keeps per-tab undo/redo (RawXmlEditorVm) + a status line; modules get light validation, Economy
  keeps the aggregated CE Dashboard validation.

## Status: ALL PHASES DONE (2026-06-15, Core 569 / Tray 186 tests green)

All seven phases below are implemented + tested + smoke-realized. The MISSION CONFIG module rail groups every
mission xml/cfg into Economy/Events/World/Server/Map files; the auto-generated map data is shortcut-only.

## Build phases (safe, additive increments — each compiles + tested + green before the next)

1. **Map files** module: new nav item + panel listing present map-data files (size) with open-folder / open-in-VS-Code
   (fallback default app) / reveal-in-Explorer, + "open mission folder". Pure arg-builders unit-tested; launch thin.
2. **cfgeconomycore (CE Routing)**: highest functional gap — the `<ce>` manifest that registers custom types/
   spawnabletypes/events files (custom files added in other tabs won't load unless routed here) + backups/logging/
   dynamic-infected (lower priority). Economy module.
3. **cfgignorelist**: flat classname add/remove (shared AutoSuggest). Economy module.
4. **cfgeventspawns** + **cfgeventgroups**: Events module (positions per event; nested group defs).
5. **cfgenvironment + env/\*_territories** + **cfgweather**: World module; move Player Spawns into World.
6. **Module reorg**: split the flat Economy TabControl into the per-module panels above under a MISSION CONFIG
   nav group; migrate existing tabs. (Done last so editors exist first; keeps the app working throughout.)
7. **Server** module: db/messages scheduler editor; cfggameplay.json later.

Patterns to reuse: `CeFileService` + `XxxXml` parser + `XxxCatalog` (closed sets) in Core; `RawXmlEditorVm`
subclass + dashboard view + `WpfRealizationSmokeTests` coverage in Tray; per-tab Refresh wired in the module panel.
