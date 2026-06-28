# Changelog

All notable changes to dzl are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the app is versioned by
git tag (`v*`), which the release workflow turns into a Velopack release.

## [0.1.19] - 2026-06-28

### Changed
- **My Mods pack groups start collapsed by default.** Packs now begin collapsed; only the ones you explicitly
  expand are remembered (still persisted across refreshes and restarts). Keeps the My Mods list compact when you
  have many packs.

## [0.1.18] - 2026-06-28

### Added
- **My Mods remembers collapsed pack groups.** Collapsing a pack's group now sticks — across list refreshes
  (build / link / import / delete) and app restarts — instead of snapping back open every time the list
  rebuilds. Stored in a small `ui-state.json` next to the config (separate from presets, so it's pure view
  state).

## [0.1.17] - 2026-06-28

### Fixed
- **Game-data extraction now unpacks vanilla only — not your mods.** The extractor enumerated PBOs recursively
  under the DayZ install and followed junctions into `!Workshop\@<mod>` (and loose `@<mod>`) folders, so a
  machine with many subscribed mods tried to unpack **thousands** of mod PBOs (e.g. 3600+) into `P:`. It now
  skips any `@`- or `!`-prefixed folder and extracts only the game's own data (`Addons`, `sakhal\Addons`, `dta`,
  …) — back to roughly the real vanilla count.

## [0.1.16] - 2026-06-28

### Fixed
- **Pack inner mods now get a unique PBO prefix.** A pack child with no `$PBOPREFIX$` of its own was packed
  with just its folder name as the prefix (e.g. a terrain child → `prefix=world`). That collides with vanilla
  and other mods and breaks loading — a map's `worldName` (`<pack>\world\<map>.wrp`) couldn't be found
  ("Cannot load world"). It now falls back to the unique pack-relative path `<pack>\<child>` (how pboProject
  derives it from the folder layout), so terrains and assets resolve. Children that already ship a `$PBOPREFIX$`
  are unchanged.

## [0.1.15] - 2026-06-28

### Added
- **Reliable in-app game-data extraction.** A new **Extract game data** action (Tools page + the first-run
  wizard) unpacks every vanilla PBO to the `P:` drive directly via DayZ Tools' `BankRev`, instead of going
  through `WorkDrive.exe`'s built-in extract. It's **incremental** — a manifest skips PBOs already extracted at
  their current version, and a **full re-extract** toggle forces everything — and it shows per-PBO progress.
  This gives dzl a controllable, scriptable extraction that doesn't depend on the WorkDrive GUI.

## [0.1.14] - 2026-06-28

### Added
- **"Build anyway" on the pack build console.** Preflight still runs and reports its findings, but its
  errors no longer block the build. This is for map/world mods that reference vanilla assets which aren't
  extracted on your `P:` drive (e.g. an `.emat` terrain/ocean material the engine resolves at runtime) — the
  resulting "missing referenced file" errors are false positives, and you can now build past them without
  turning off preflight globally. (`BuildService.Build`/`BuildPack` gained an `ignorePreflightErrors` option.)

## [0.1.13] - 2026-06-27

### Changed
- **Builds pack the PBO in-process — no more FileBank/AddonBuilder.** A new built-in PBO writer assembles the
  `.pbo` directly (stored/uncompressed, with the `$PBOPREFIX$` and a SHA-1 trailer), so a build never depends on
  an external packer or the DayZ file server for packing. This removes a whole class of hangs and naming quirks
  and makes packing deterministic.

### Fixed
- **Binarize now gets the project-drive context it needs — no more hangs, no more "Material not loaded".**
  `binarize.exe` is invoked with `-binpath`/`-addon` pointed at the mounted `P:\` work drive (plus `-always`,
  `-silent`, `-maxProcesses`, `-textures`) and run with its working directory set there. With that context it
  resolves vanilla **and** the mod's own materials (so a vehicle/model mod no longer floods the log with
  "Material not loaded …rvmat"), runs roughly an order of magnitude faster, and accepts staging that lives off
  the work drive — which is what made model mods (e.g. a Land Rover) appear to never finish.
- **Build doesn't run binarize when there's nothing to binarize.** A mod with no MLOD models (config/script-only,
  or only already-binarized ODOL models) skips Binarize entirely instead of churning for ~35s per mod — so a
  pack of mostly-config mods builds in seconds and the log stays clean.
- **Captured process runs can't deadlock on a lingering child.** `binarize.exe` spawns a persistent file-server
  child that inherits the output pipe; the process runner now waits on the process handle (not the streams' EOF)
  with a bounded drain, so a build no longer hangs after binarize itself has finished.

## [0.1.12] - 2026-06-27

### Changed
- **New build engine (no AddonBuilder).** Mod and pack builds now run a direct DayZ-Tools pipeline —
  `binarize.exe` → `CfgConvert` (config.cpp→config.bin) → `FileBank.exe` (pack) → `DSSignFile` (sign) —
  giving per-file control AddonBuilder didn't expose.

### Fixed
- **Already-binarized (ODOL) p3d no longer crash the build.** Such models are excluded from Binarize and
  shipped unchanged (verified byte-identical in the output PBO), while the rest of the mod still binarizes
  normally — so a mod/pack containing pre-binarized models builds instead of dying with an access
  violation (0xC0000005). AddonBuilder's `-include` cannot do this (confirmed by testing).

## [0.1.11] - 2026-06-27

### Fixed
- **Pack build console — scrollable mod list.** The "Mods to build" list is now height-capped with
  a scrollbar, so a pack with many mods no longer pushes the options and build log off-screen.

## [0.1.10] - 2026-06-27

### Changed
- **Pack build console — tidier mod list.** "Mods to build" is now a proper selectable list: each
  mod is a row with a checkbox, name and a marker chip (`config.cpp` / `$PBOPREFIX$`), with a
  "select all" toggle + a selected count, and the build options (binarize / sign / key) separated
  below a divider.

## [0.1.9] - 2026-06-27

### Added
- **Mod packs on My Mods.** A folder whose subfolders are each a mod (own `config.cpp` /
  `$PBOPREFIX$`) is auto-detected as a *pack* and shown as an expandable group (named after the
  folder, with git at the pack level) instead of being invisible. One level of nesting.
- **"Build pack…".** Build a pack's inner mods into one `@<pack>` — a PBO per mod under `Addons\`
  plus a shared `keys\`, published atomically and registered as a single loadable mod. The console
  lets you pick which inner mods to build (all by default), choose binarize/sign + key, and shows
  per-mod preflight tabs with the same findings UX as the single-mod build (severity badges,
  clickable `file:line`).

### Fixed
- **Wrong PBO prefix when building a pack child.** AddonBuilder derived the prefix from the nested
  source's leaf folder; the child's own `$PBOPREFIX$` is now passed explicitly (`-prefix=`), so
  assets resolve at the right root.
- **Preflight false-positive on multi-segment `$PBOPREFIX$`.** `cfgmods-folder-unreferenced` (and
  include/path resolution generally) only stripped the first prefix segment, so a mod with a prefix
  like `Mod\Core` was wrongly flagged. References that open with the whole prefix now resolve.

## [0.1.8] - 2026-06-27

### Added
- **Search box on the mod-selection lists.** The server editor's Mods tab now has a filter
  (shared with the main Mods page), and the My Mods projects list filters by name / path.

### Fixed
- **The "claude mcp add" command on the MCP page pointed at a missing path on the installed
  app.** The MCP server ships isolated in an `mcp\` subfolder as a self-contained
  `dzl-mcp.exe` (its .NET 10 deps would poison the net8 Tray if merged into the root). The
  command now resolves `mcp\dzl-mcp.exe` instead of a non-existent `Dzl.Mcp.dll` in the root.

## [0.1.7] - 2026-06-27

### Fixed
- **Per-instance `serverDZ.cfg` is now actually honored.** The server is launched with
  an absolute `-config` path (accepted by DayZ 1.29). The previous approach relied on the
  working directory, but the engine forces `$currentdir` to the exe folder — so every
  instance silently loaded the DayZ *install's* `serverDZ.cfg` instead of its own.
- **Per-instance mission / Central Economy is now honored.** A server's `serverDZ.cfg`
  mission `template` is repointed at the instance's own `mpmissions` (absolute path) when
  the instance is created and on every launch, so the engine loads the instance's mission
  — the one dzl's CE editor manages — not the install's.
- **New servers no longer inherit a previous instance's mission.** Creating an instance
  repoints its `Mission` at its own folder instead of copying the active preset's value
  (which could be an absolute path to a different instance).
- **Folder / file pickers open where the field points.** Across the server editor,
  settings, tools, add-mod, module settings and key import, the "browse" buttons now start
  in the directory the field already contains (the parent directory for file fields),
  falling back to the projects root / DayZ folder — instead of always jumping to the DayZ
  install.

### Added
- **Dashboard "Mission source" card.** Shows which `mpmissions` folder the server will
  actually load (instance / install / missing), read from `serverDZ.cfg`, with a one-click
  "Fix" that repoints the template at the instance's own mission.

[0.1.19]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.19
[0.1.18]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.18
[0.1.17]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.17
[0.1.16]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.16
[0.1.15]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.15
[0.1.14]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.14
[0.1.13]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.13
[0.1.12]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.12
[0.1.11]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.11
[0.1.10]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.10
[0.1.9]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.9
[0.1.8]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.8
[0.1.7]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.7
