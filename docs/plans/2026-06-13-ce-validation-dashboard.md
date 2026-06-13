# CE Validation framework + Economy Dashboard

Date: 2026-06-13. Goal: a Central-Economy **validation framework** surfaced two ways —
(1) **per-screen** light checks that run live as you edit a CE file, and (2) a new
**Dashboard** tab (first in the Economy window) that aggregates everything: stats tiles,
a validation summary, shortcuts, and a **"Run full validation"** button (heavy cross-file
pass) with a progress bar.

Owner decisions (agreed): no classname DB (we can't read binarized config.cpp, so item
class existence is out of scope — only CE-internal relations); validations live per-screen,
the dashboard sums them; heavy full pass on a button with a loading bar; rule values grounded
in DayZ docs (RAG verified per-rule during implementation).

## Architecture (extends the existing `Dzl.Core.Economy.Lint`)

Today: `ICeRule.Check(CeFileSet, LimitsDef) → LintFinding`, `LintEngine`, `LintFinding`
(Severity/Code/Message/File/EntryName), `CeFileSet` (Types entries only), `TypesRules`.
We generalize from "Types only" to "the whole CE world".

- **`CeWorld`** (new, `Dzl.Core.Economy`): the loaded mission CE — `Types` (List<TypeEntry>),
  `Events`, `Globals`, `SpawnableTypes`, `RandomPresets`, `PlayerSpawns`, `Limits` (base
  `LimitsDef`), `UserGroups` (List<LimitsUserGroup>), plus `Files` (which CE files resolved
  vs missing, with paths). Loaded by **`CeWorldLoader`** from the active mission (reuses the
  existing per-file services' `Load()` + `MissionLocator`). Never throws — missing/malformed
  files yield empty lists + a "file missing/unparsable" finding.
- **`CeFinding`** (supersedes/extends `LintFinding`): `Severity, Code, Message, File, Entry,
  Field, Kind (CeKind enum: Types/Events/Globals/SpawnableTypes/RandomPresets/PlayerSpawns/
  Dictionaries), Target (entry id for the editor to jump to)`. Keep `LintFinding` as the same
  record (extend in place) so existing TypesRules/TypesService callers keep compiling.
- **`ICeRule`** → `Check(CeWorld) → IEnumerable<CeFinding>`, each rule exposes a `Scope`:
  `PerFile(CeKind)` (light, runs live in that editor) or `CrossFile` (heavy, full pass only).
- **`CeValidator`** (new facade): `Validate(CeWorld, scope, IProgress<int>?)` runs the matching
  rules. Per-screen calls `Validate(world, PerFile(kind))` on the in-memory edited data (fast,
  no disk). Full pass runs all rules with progress.
- Frontend facade: `CeValidationService(configPath)` — `LoadWorld()`, `ValidateFull(progress)`,
  `ValidatePerFile(kind, data)`. Returns plain records.

## Rule catalog (the "full" validation)

Grounded in CE knowledge + DayZ wiki (RAG). Items marked **[RAG]** get a targeted RAG check
during implementation before the exact allowed-set is hardcoded.

**types.xml (PerFile + cross to Dictionaries):**
- name non-empty; **duplicate** name across merged files (error — last wins silently in-game).
- nominal/min/lifetime/restock/cost int ≥ 0; quantmin/quantmax = -1 or 0–100.
- min ≤ nominal (warn); quantmin ≤ quantmax; quant pair both -1 or both set.
- flags (count_in_*/crafted/deloot) ∈ {0,1}.
- category ∈ cfglimitsdefinition `<categories>`; usage[] ∈ usageflags ∪ user-lists; value[] ∈
  valueflags ∪ user-lists; tag[] ∈ tags. *(most already in `TypesRules` — port + extend)*
- nominal 0 with min 0 (item never spawns — info).

**cfglimitsdefinitionuser ↔ definition:** each user-list member exists in base; duplicate
user-list names.

**cfgspawnabletypes ↔ cfgrandompresets ↔ types (cross-file):**
- block `preset=` exists in cfgrandompresets **of the same kind** (cargo→cargo, attachments→
  attachments) — error if missing.
- chance ∈ [0,1]; damage min/max ∈ [0,1], min ≤ max.
- type `name` exists in types.xml (warn — likely typo; not error, could be a non-CE class).
- (inline item classnames — **skipped**, no class DB.)

**cfgrandompresets:** chance ∈ [0,1]; item chance ∈ [0,1]; name unique within kind; preset
referenced by no spawnabletype (info — unused).

**events.xml ↔ types (cross-file):**
- name unique; nominal/min/max/lifetime/restock/radii int ≥ 0; min ≤ max; nominal ∈ [min,max].
- flags + active ∈ {0,1}.
- position ∈ allowed set **[RAG]** (fixed/random/player?); limit ∈ allowed set **[RAG]**.
- child `type` exists in types.xml (warn); child min/max/lootmin/lootmax int ≥ 0.
- **children min should sum to 100** (spawn-weight %, per wiki) — warn if not.

**globals.xml:** name ∈ known CE globals **[RAG — exact var list]** (else warn unknown);
type ∈ {0,1}; value numeric; count-type vars ≥ 0; required globals present **[RAG]**.

**cfgplayerspawnpoints:** structure valid; each category (fresh/hop/travel) has ≥ 1 group with
≥ 1 pos (warn if empty); pos x/z numeric (map-bounds check skipped — no map size).

**File presence:** each expected CE file resolves under the mission; missing → info/warn tile.

## UI

**Dashboard tab (first in `EconomyPanel.xaml`):** new `EconomyDashboard` UserControl + VM
(sub-VM under MainViewModel, same pattern as the CE editor sub-VMs).
- **Header:** active server + resolved mission path + "open mission folder".
- **Stat tiles** (grid, each clickable → selects that editor tab): Types (total + vanilla/mod/
  custom), Events, Globals, Spawnable Types, Random Presets, Player-spawn groups, Dictionary
  names (usage/value/tag/category). Missing file → "—".
- **Validation summary card:** N errors / M warnings (aggregated from the last full run +
  live per-screen counts); list grouped by file; click a finding → jump to editor+entry.
  **"Run full validation"** button → runs the cross-file pass off the UI thread with a
  **progress bar** (IProgress); re-runnable.
- **Shortcuts card:** open mission folder, types backups, refresh stats, open each file.

**Per-screen:** each editor runs its `PerFile(kind)` rules on edit (debounced) → a small
finding-count badge on its tab header + an inline list/strip. Light = live; heavy stays on the
dashboard button.

## Phasing (each: build + tests + commit)

1. **Core engine** — `CeWorld` + `CeWorldLoader` + extend `CeFinding`/`ICeRule`/`LintEngine`
   + `CeValidator` + per-file & cross-file rules (RAG-verify the [RAG] items). Unit tests per
   rule (pure, table-driven). No UI yet.
2. **Dashboard tab** — stats tiles + validation summary + shortcuts + full-validation button +
   progress, aggregating the engine. First tab in EconomyPanel.
3. **Per-screen live counters** — wire each editor's PerFile validation → tab badge + inline.

## Constraints

Dzl.Core zero deps; rules pure + table-tested; loader/validator never throw; no class DB;
heavy pass off the UI thread with progress; reuse existing per-file services + MissionLocator;
keep `LintFinding`/`TypesRules`/`TypesService.Lint` compiling (extend, don't break).
