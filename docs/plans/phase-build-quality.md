# Phase — Build Quality: Preflight, Diagnostics, Skip-Unchanged

> **For agentic workers:** Use subagent-driven-development. Steps use `- [ ]`.

**Goal:** Raise the build pipeline from "wrap AddonBuilder and hope" to "validate before,
diagnose after, skip what didn't change." Functional spec distilled from studying how the
community build tools approach DayZ packing (notably RaG PBO Builder's feature set); all
implementation is ours, in `Dzl.Core` conventions (pure helpers + tests, wrappers never throw).

**Why:** AddonBuilder is a black box: it fails late, its log is noisy, and a "successful"
build can still ship a broken PBO (missing textures, bad prefix, config that won't parse).
Community tooling proves the pain points are predictable and checkable *before* the build —
and explainable after a failure by pattern-matching the log.

---

## What we learned (functional spec, no code carried over)

### 1. The build is a verification pipeline, not a pack call

The mature flow is: **preflight → stage (filtered copy) → binarize → config convert →
pack → verify pack → sign → verify signature → publish atomically → record state hash**.
Every arrow is a checkpoint that can fail with a *specific* message. Our current flow is
junctions → AddonBuilder → "did a fresh .pbo appear?" — one checkpoint.

Key downstream checks worth having even with AddonBuilder doing the middle:

- **Post-pack prefix check:** read the `prefix` property back out of the produced PBO header
  (PBO header format is public/documented) and compare with the project's `$PBOPREFIX$`.
  Mismatch = mod loads but every asset path is wrong. Cheap to read (first 64 KB).
- **Post-sign check:** a `.bisign` matching `<pbo>.<keyname>.bisign` must exist next to the
  PBO when signing was requested. AddonBuilder `-sign` can silently not sign.
- **Fresh-output check:** we already have this (`ModBuild.HasFreshPbo`). Keep.

### 2. Preflight catches ~90% of "why is my mod broken" before building

Checks that apply to our item/script mods (each = one pure rule over the project dir):

**Config layer** (parse `config.cpp` after stripping comments; resolve `#include` recursively):
- `CfgPatches` class exists, contains ≥1 patch class, `requiredAddons[]` present
  (missing → undefined load order; empty is legal but worth an info note). Official docs
  ([Modding Structure](https://community.bistudio.com/wiki/DayZ:Modding_Structure)) confirm
  CfgPatches is *the only required part of a PBO*.
- `CfgMods` is **required whenever the PBO adds/modifies scripts or inputs**, and the mod
  class needs `type = "mod"` (per Modding Structure). Optional members worth validating when
  present: `inputs`, `skeletonDefinitions`, `dependencies[]` (e.g. `{"Game"}`), and
  `defs > imageSets/widgetStyles files[]` — all are packed-path references that must resolve.
- Heuristic: classes inheriting from known vanilla bases (`Inventory_Base` → `DZ_Data`,
  `Clothing_Base` → `DZ_Characters`, weapon/vehicle/food bases…) imply `requiredAddons`
  entries; hint when absent.
- `CfgMods` ↔ `scripts/` folders cross-check: every `scripts/3_Game`-style folder present on
  disk must be referenced by the matching script-module `files[]` entry, and every `files[]`
  path must exist. Mismatch = scripts silently never compile. (We can cross-check against our
  RAG: module names `engineScriptModule`…`missionScriptModule` map to `1_Core`…`5_Mission`.)
- Syntax gate: run CfgConvert `-bin` to a temp file per `config.cpp`; non-zero exit or no
  output = config error with the tool's own message attached. This is the single
  highest-value check — it front-runs the most common crash.

**Reference layer** (scan text files: `.cpp .hpp .h .c .rvmat .cfg .xml .json .layout .imageset`):
- Any quoted string ending in a risky extension (`.paa .rvmat .p3d .wss .ogg .wav .edds …`)
  is a reference; resolve it against (a) project dir, (b) project parent, (c) prefix-relative,
  (d) P:\ root. Missing → error. Resolving but *excluded from the PBO* → error too (exists on
  disk, won't exist in the PBO — nastiest variant).
- Subtleties that matter (learned the hard way by others):
  - Strip `{GUID}` prefixes (`{03C79F5D…}path/file.edds`) — Workbench resource IDs, not paths.
  - Skip references built by string concatenation in scripts (`"path" + variable`).
  - Skip `#include` lines in the reference scan (build-time, not runtime).
- `.rvmat` files get a dedicated `texture=...` scan; a `.png`/`.tga`/`.psd` in an rvmat
  (instead of `.paa`) = warning — works in Workbench, broken in the packed mod.
- **Baked-in absolute drive paths** (`P:\...` or any `X:\` in `model =`, `texture=`,
  `hiddenSelectionsTextures[]`, rvmat `texture=`): **error**. They resolve on the dev box
  (P: mounted) and break on every other machine — the nastiest ship-it bug because local
  testing passes. (From our agentic-z production debugging notes; reference resolvers that
  happily resolve absolute paths miss this completely.)
- Texture suffix convention: packed `.paa`/`.png`/`.tga` textures should end `_co`/`_nohq`/
  `_smdi` (engine assigns texture role by suffix) — warning when missing. rvmat stage check:
  diffuse/normal/specular stages expect the matching suffix.
- `hiddenSelections[]` vs `hiddenSelectionsTextures[]`/`hiddenSelectionsMaterials[]` arity
  mismatch in a class = warning (selection without texture or vice versa). Deep validation
  (selection names actually exist in the `.p3d` Visual LOD) stays in the `dayz-p3d-audit`
  skill — needs a p3d parser, out of dzl scope.
- `.p3d` files: scan the binary for printable strings ending in asset extensions, resolve
  each as a *warning*-level reference (false positives possible, hence not error).

**Filesystem layer:**
- Case-only duplicate paths (`Data/foo.paa` vs `data/Foo.paa`) — tools are case-fuzzy,
  game/Linux servers aren't.
- **Lowercase rule (official, Linux servers):** the mod's `addons`/`keys` folders and every
  file inside `addons` should be lowercase or the mod breaks on DayZ Linux Server binaries
  ([Modding Basics → Packing the Mod](https://community.bistudio.com/wiki/DayZ:Modding_Basics)).
  Preflight: warn on any uppercase character in a to-be-packed relative path; build: name the
  output `.pbo` lowercase. Community tools don't check this one at all.
- **mod.cpp presentation check** (mod root, outside the PBO): `name`, `author`, `version`
  present; `picture`/`logo`/`logoSmall`/`logoOver` paths resolve to packed files
  (fields per Modding Structure → Mod presentation). Missing file = broken main-menu tile.
- Windows-invalid or non-ASCII characters anywhere in a packed path; PBO paths must encode
  to ASCII. Path length > ~240 chars.
- Texture freshness: a `.png`/`.tga` source sitting next to an older or missing `.paa` =
  warning ("you edited the texture and forgot ImageToPAA"). Our `convert_paa` tool closes
  the loop: offer to convert.
- ODOL detection: a `.p3d` whose first 4 bytes are `ODOL` is already binarized; feeding it
  to Binarize crashes it with `0xC0000005`. Detect, warn, and (in the build) make sure the
  binarize step skips it but the file still ships unchanged.

**Script layer** (Enforce `.c` — cheap textual checks, not a parser):
- `modded class X extends Y` / `modded class X : Y` — the base clause on a modded class is
  a silent no-op trap (we have this in memory/agentic-z too). Warning + suggested fix.
- Duplicate non-modded `class X` definitions across the mod's scripts — usually someone
  meant `modded`.
- `SetActions()` override that never calls `super.SetActions()` — wipes inherited actions.
- Bracket/quote balance sanity scan (string/comment-aware) — catches truncated files.

**Known-vanilla-trap lint** (config.cpp patterns from agentic-z production notes — cheap
regex rules, high value because all of these fail *silently* in game):
- `inventorySlot[] += {...}` — vanilla declares `inventorySlot` as a *string* on many items
  (Bohemia ticket `T148506`, won't-fix); array-append onto a string is silently dropped.
  Fix: redeclare the full array including the original value.
- `healthLevelValues[]` (legacy) instead of `healthLevels[]` nested under
  `DamageSystem > GlobalHealth > Health` — parses, does nothing.
- `autocenter = 0` on a handheld/kit class (floats when dropped) vs missing on a placed
  `Inventory_Base` object (buries in ground) — heuristic info-level note both ways.

**Report:** every finding = `(severity, rule, message, file, line)`. Counters per category.
Export `.txt` + `.json` next to the build log. Errors block the build (configurable);
warnings don't.

### 3. Failure diagnostics: pattern → cause → fix

Keep a ring buffer of the last ~500 log lines; on failure, match known patterns and print
"likely cause / what it means / suggested fix" entries. Patterns worth shipping day one:

| Log pattern | Likely cause |
|---|---|
| `cannot include file`, `preprocessor failed` | `#include` not resolvable at build path |
| `0xC0000005`, `access violation` | ODOL P3D fed to Binarize / corrupt source |
| `error 3 while parsing config`, CfgConvert non-zero | config syntax error (incl. in included .hpp) |
| no `.bisign` after sign step | bad/missing private key, AV locking the file |
| ImageToPAA non-zero | unreadable/unsupported source texture |
| output unchanged + exit 0 | stale path/junction, AddonBuilder no-op |

Also: summarize tool output (count error/warning/missing/texture lines) so the tray can show
"Binarize: 3 errors, 12 missing references" instead of a 400-line wall.

**Client-kick decoder (dzl-specific bonus):** the official
[Error Codes](https://community.bistudio.com/wiki/DayZ:Error_Codes) table maps verification
kick codes to packaging causes — `VE_MISSING_BISIGN` (0x7E: PBO without .bisign),
`VE_PATCHED_PBO`, `VE_INTEGRITY`, `VE_EXTRA_MOD`/`VE_MISSING_MOD`, `VE_UM_CLIENT_UPDATED`
(version skew). We already tail the client/server logs — when a connect fails with one of
these, the log diagnoser should name the cause and point at the offending mod. Same
pattern→cause→fix table, different input stream.

**AddonBuilder include-list gap:** Addon Builder only packs known file types by default —
the official terrain tutorial explicitly tells users to add `*.xml` and `*.nm` to the
"files to copy directly" option or they're silently dropped. Our wrapper passes no include
list today. Preflight: collect extensions of all *referenced* files and warn when one falls
outside AddonBuilder's default include set; build: pass the extra patterns through
(AddonBuilder CLI supports an include-patterns file).

**AddonBuilder `-temp=` isolation:** our agentic-z build skill passes
`-temp=P:\temp\<Mod>` per mod (kept on failure for debugging, cleaned on success);
our `AddonBuilder.PackArgs` passes no temp today, so concurrent/sequential builds share
AddonBuilder's default temp and stale state can leak between mods. Add a per-mod temp arg.

**Dev-file exclusion:** our own scaffolds put dev-only files inside the project dir
(`workbench/` + `*.gproj`, `README.md`, `.gitignore`, `.gui-sources/` from the imageset
pipeline, `.git/`). Without an exclude list AddonBuilder may pack them. Preflight: warn when
a known dev-only file/folder would be packed; build: ship a default exclude set (the
imageset convention is explicit: sources stay in `.gui-sources/`, only the packed
`gui/imagesets/*.{imageset,paa}` ships).

### 4. Skip-unchanged via content hash, not timestamps

State hash per mod = SHA1 over: every packed file's (relpath, size, mtime_ns, content-sha1) +
the build settings that affect output (binarize on/off, sign on/off, key fingerprint, tool exe
fingerprints, prefix). Cache `(mod → hash, pbo path, timestamp)` in a JSON next to our config.
On build: same hash + PBO exists (+ signature exists if signing) → skip. `--force` bypasses.
Per-run memoization of file SHA1s (keyed by path+size+mtime) keeps rehashing cheap; don't
persist source hashes across runs (mtime+size guard is enough within one run).

Content-based matters because git checkouts/touches change mtimes without changing content —
timestamp-only caches rebuild constantly or, worse, skip wrongly.

### 5. Atomic publish (don't break the loadable mod on a failed rebuild)

Build into a temp work dir next to the output; only after pack+verify+sign succeed, swap into
place (`File.Replace`/backup-then-move), restoring the previous PBO+bisign on any failure.
Today a failed AddonBuilder run can leave `@Mod/Addons` half-written while the server is
configured to load it. Cheap to add since our output dir is ours (`build\@<Mod>\Addons`).

Signing detail: sign in an isolated temp folder (copy PBO + key in, run DSSignFile on base
names, copy `.bisign` back) — avoids cwd/path-length quirks and stale-signature pickup;
delete older `.bisign` files for the same PBO when publishing a new one.

### 6. Out of scope for us (noted for completeness)

- **Terrain/WRP pipeline** (project-prefix junction staging for Binarize, worldName ↔ prefix
  ↔ packed-entry triangulation, layer-rvmat checks, navmesh checks, source/export-folder size
  guards): valuable but maps-only. We have no map users yet. Park it; the junction trick is
  already native to our P:-centric design if we ever need it.
- **Own PBO writer/reader:** community proves a standalone packer is feasible (uncompressed
  entries + `prefix` property + SHA1 trailer; LZSS only needed for *reading* old PBOs). We
  don't need it while AddonBuilder works — but a small *reader* (header walk: properties +
  entry table) is cheap and is exactly what the post-pack prefix/entry verification needs.
  We already ship `unbinarize`/inspection adjacent features; the reader can live in
  `Dzl.Core.Build.PboHeader`.
- **Deep `.p3d` validation** (LOD inventory, face winding, `Component01` casing, memory
  points, watertightness, collision-LOD material assignment): stays in the `dayz-p3d-audit`
  skill (py3d-based, Claude-driven). dzl preflight keeps only the cheap binary checks
  (ODOL magic, printable-string references). If dzl ever parses p3d LODs, remember DayZ's
  LOD resolutions differ from Arma 3's (ViewGeometry `6e15`, FireGeometry `7e15` — Arma
  values floating around the internet misclassify them).
- **GUI path presets** — our config presets already cover this differently.
- **PAA auto-update during build** (`update_paa_from_sources`): consider later as an opt-in
  build flag that calls our existing `ImageToPaa` for stale textures found by preflight.

---

## Mapping to dzl architecture

```
src/Dzl.Core/Build/
  Preflight/
    PreflightEngine.cs      # walks a project dir, runs enabled rules, returns PreflightReport
    PreflightReport.cs      # records: Finding(severity, rule, message, file, line), counters
    Rules/                  # one file per rule family, all pure where possible
      ConfigRules.cs        # CfgPatches / requiredAddons / CfgMods↔scripts
      ReferenceRules.cs     # quoted-path scan, rvmat textures, p3d strings
      FileSystemRules.cs    # case conflicts, invalid paths, texture freshness, ODOL
      ScriptRules.cs        # modded-class clause, duplicate classes, SetActions super
    CppText.cs              # strip comments, class-block walker, array parser, include resolver
  PboHeader.cs              # read prefix + entry table from a PBO (verification only)
  BuildCache.cs             # state hash + cache load/save (JSON next to config)
  BuildDiagnostics.cs       # pattern table + log ring buffer -> diagnosis list
  ModBuild.cs               # (existing) + atomic publish helpers
```

- `BuildService.Build` gains: optional preflight gate (config flag `preflight_before_build`,
  default warn-only), cache check before AddonBuilder, diagnostics on failure, post-pack
  prefix/signature verification, atomic publish.
- `LauncherService` gains `Preflight(mod)` returning the report; surfaced as:
  CLI `dzl preflight <mod>`, MCP tool `preflight`, tray button on the mod card + findings
  panel (reuse the lint-findings UI pattern from the Economy editor).
- **Tray placement:** a separate modeless **Build window** (own VM, opened from the mod card /
  Tools tab, NO `Owner` — see the WPF-UI owned-window bug), following the Economy/Git/Workshop
  window pattern. MainViewModel only gets the "open" command. Logic stays in `Dzl.Core`
  (CLI + MCP are first-class consumers — "Claude runs preflight" is a primary use-case);
  a separate assembly/plugin module is *not* worth it at this scale — namespace + window
  give the same isolation without the ceremony.
- Per-check enable flags live in config (snake_case), one `preflight_checks` object;
  `ConfigStore.Migrate` untouched (new keys, no renames).
- All rules pure → unit tests with in-memory/string fixtures (xunit + FluentAssertions),
  same style as `ArgvBuilder` / `WorkDrive` arg-builder tests. CppText parser gets
  property-style tests (comments inside strings, nested braces, includes cycle guard).

## Suggested task order

- [x] 1. `CppText` (comment strip, class walker, `parse array`, include resolve) + tests
- [x] 2. `PreflightReport` + `PreflightEngine` skeleton + ConfigRules (CfgPatches/CfgMods) + CfgConvert syntax gate
- [x] 3. ReferenceRules (text scan + rvmat + exclusion-aware resolve) + FileSystemRules
- [x] 4. ScriptRules + report export (txt/json)
- [x] 5. Surface: `BuildService.Preflight` → CLI (`dzl preflight`) / MCP (`preflight`, `diagnose_logs`) / tray (BuildWindow)
- [x] 6. `BuildCache` (state hash + skip) into `BuildService.Build` + `--force`
- [x] 7. `BuildDiagnostics` + tool-output summary into build failure path
- [x] 8. `PboHeader` reader + post-pack prefix/signature verification + atomic publish

All implemented + E2E-verified against the real AddonBuilder (2026-06-11). Field notes from the
live runs: `-temp=` must point at an EXISTING dir (binarize dies at "Syncing folders" otherwise);
`-include=` takes ONE semicolon-separated line (newline-separated lists fail the binarize path);
AddonBuilder can exit 0 with "Build failed" in the log — the fresh-pbo check is the real gate.
