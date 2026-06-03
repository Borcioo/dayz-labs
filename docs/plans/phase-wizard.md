# Environment Setup Wizard — Plan

**Goal:** A first-run (and on-demand) multi-step wizard that takes a fresh machine to a working local DayZ dev environment: detect/confirm DayZ + DayZ Tools, mount the P: work drive, extract vanilla game data, scaffold a server instance (serverDZ.cfg + profiles + mission), optionally fetch the dedicated server via SteamCMD (guided), and set mod scan-roots — then save the config.

**Split:**
- **W1 (Core, TDD):** `EnvDetect` (Steam library + app folder detection), `ServerScaffold` (serverDZ.cfg template, profiles dirs, mission copy), `SteamCmd` (script generation). Pure parts unit-tested; filesystem/registry/process parts thin + manual.
- **W2 (WPF):** `SetupWizardWindow` — stepper UI driving W1 + existing `WorkDrive`/`ToolCatalog`/`ToolLauncher`/`ModDiscovery`; launches on first run (no config / empty) and from a Settings/menu button. Writes the resulting `DzlConfig`.

## W1 — Core helpers (src/Dzl.Core/Env/)
- `EnvDetect.cs`:
  - `SteamPath()` — from registry (HKCU\Software\Valve\Steam\SteamPath, fallback HKLM WOW6432Node InstallPath). Windows-only, manual.
  - `ParseLibraryFolders(string vdfText) -> List<string>` — extract library `path` entries from `libraryfolders.vdf`. **TESTED.**
  - `FindApp(IEnumerable<string> libraries, string relFolder) -> string?` — first `<lib>\steamapps\common\<relFolder>` that exists. **TESTED** (fake tree).
  - Known folders: DayZ=`DayZ`, Tools=`DayZ Tools`, Server=`DayZServer`.
  - `Detect()` -> record {DayzPath?, ToolsPath?, ServerPath?} composing the above (manual top-level).
- `ServerScaffold.cs`:
  - `string DefaultServerCfg(string missionName="dayzOffline.chernarusplus")` — returns a sane dev serverDZ.cfg text. **TESTED** (contains hostname, Missions/template, verifySignatures=0).
  - `Scaffold(string dayzPath, string instanceDir)` — writes serverDZ.cfg (if absent), creates `profiles`/`profiles_client`, copies the mission from `<dayzPath>\mpmissions\<mission>` into `<instanceDir>\mpmissions\` if present. Returns a report. Manual (filesystem); the cfg-text + path composition pure-tested.
- `SteamCmd.cs`:
  - `string DownloadServerScript(string serverDir, string steamUser="YOUR_STEAM_LOGIN")` — returns a steamcmd script line/batch for app 223350 with validate. **TESTED** (contains 223350, force_install_dir, the dir, validate).

## W2 — SetupWizardWindow (src/Dzl.Tray/)
Stepper (Wpf.Ui) with Back/Next/Finish + a left step list. Steps:
1. **Welcome** — explains it'll set up the local dev environment.
2. **Paths** — runs `EnvDetect`, shows found DayZ/Tools (green check) or Browse to set; required: DayZ. Tools optional but warns (needed for debug/extract).
3. **Work drive** — `WorkDrive.IsMounted()`; Mount button (`WorkDrive.Mount(<tools>\Bin\WorkDrive\WorkDrive.exe)`). Skippable.
4. **Game data** — detect if P: has vanilla data (e.g. `P:\dz` or known folder); button "Open DayZ Tools to Extract Game Data" (launch `ToolCatalog.Find(tools,"launcher")` or the Workbench/launcher) with a one-line how-to; mark done/skip.
5. **Server instance** — pick/confirm an instance dir (default = DayZ dir), `ServerScaffold.Scaffold` to write serverDZ.cfg + profiles + mission. Show the report.
6. **Server files (optional, prod)** — note dev uses DayZDiag -server (no separate server needed); if user wants the dedicated server, show the `SteamCmd.DownloadServerScript` text + Copy button + how-to. Skippable.
7. **Mods** — set scan-roots (prefill detected P:\ + common), Rescan preview count.
8. **Finish** — build the `DzlConfig` (dayz/tools/profiles/exe defaults/scan-roots/mission/config_name), save via `ConfigStore.Save`/active profile, close; main window reloads.

Launch points: App.OnStartup shows the wizard when config.json doesn't exist yet (first run) — BEFORE creating the main window; and a "Setup environment…" item in the nav/menu + a Settings button to re-run.

## Acceptance
- `dotnet test` green incl. EnvDetect/ServerScaffold/SteamCmd unit tests.
- Wizard builds; fresh run (no config) shows it; finishing writes a usable config; existing config skips it.
- Build 0 warnings.
