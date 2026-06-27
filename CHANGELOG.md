# Changelog

All notable changes to dzl are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); the app is versioned by
git tag (`v*`), which the release workflow turns into a Velopack release.

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

[0.1.7]: https://github.com/Borcioo/dayz-labs/releases/tag/v0.1.7
