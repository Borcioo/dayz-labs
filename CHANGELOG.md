# Changelog

All notable changes to dzl are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the app is versioned by
git tag (`v*`), which the release workflow turns into a Velopack release.

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

[0.1.9]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.9
[0.1.8]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.8
[0.1.7]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.7
