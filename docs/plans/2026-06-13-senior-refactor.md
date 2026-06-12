# Senior refactor pass — MVP → production grade

Date: 2026-06-13. Source: 4-area code review (Core App/Economy, process wrappers,
CLI/MCP/IPC, Tray). Goal: extract shared code, add the missing abstractions, fix the
latent bugs the duplication caused, keep every public behavior identical unless the
change is itself a bug fix.

## Design decisions

**One shared process runner (`Dzl.Core.Procs.ProcRunner`).** Seven divergent
"run process, capture output" implementations exist; only `Git/Proc.cs` has the
hardening (stdin redirect+close, SpawnLock, timeout + kill-tree, async readers).
ProcRunner lifts that hardening into one API; wrappers become thin adapters.
`ProcessElevation` (de-elevation primitive) and fire-and-forget launchers stay out.

**One CE file-service base (`CeFileService`).** Events/Globals/SpawnableTypes/
PlayerSpawns/RandomPresets services copy the same ~95-line block (Mission resolve,
ReadRaw, WriteRaw, Edit-with-snapshot). Base class owns the plumbing; subclasses own
domain verbs only. Public method names stay as thin aliases.

**One CE XML helper (`CeXml` + `CeNum`).** ParseDoc/Serialize shared by all 8 XML
classes (fixes the TypesXml missing null-declaration guard — latent bug); ByName/
RenameByName extensions kill ~10 copies of find/rename-with-clash-check; CeNum
standardizes invariant-culture parse/format (EventsXml was culture-sensitive).

**One IPC method table (`IpcMethods`).** Method-name constants + dispatch table
binding method → LauncherService call. IpcDispatcher and ControlPlane's direct
fallback both consume the table; adding a method = 1 const + 1 entry + frontends.

**MCP goes through ControlPlane.** Documented architecture says CLI *and* MCP route
through ControlPlane; MCP routed zero methods (split-brain with a live tray). The 9
routed MCP tools switch from `LauncherService` direct to `ControlPlane`.

**Junction anchor helper.** CLI anchors mod junctions on the work-drive *source*
(survives P: unmounts); MCP used `P:\` — drift bug. One Core helper, both use it.

## Phases (each independently shippable; build + tests after each)

1. **CE pure layer**: `CeXml` (ParseDoc/Serialize, ByName, RenameByName,
   SetChildValue), `CeNum`; migrate the 8 `*Xml.cs`; TypesXml.ToXml fix.
2. **CE services**: `CeFileService` base; migrate the 5 file services +
   DictionaryService EditLimitsUser dedup.
3. **ProcRunner**: new runner + tests; migrate CfgConvert, DsTools, ImageToPaa
   (fixes throw + deadlock), ProcessManager.ImageOf, ToolLauncher.Launch
   (never-throw), Junction/WorkDrive sub-runners; Proc.cs becomes adapter.
4. **Robustness**: ToolCatalog.Find known-table fast path (kills per-call recursive
   glob); StateFile IOException tolerance + atomic write; ProcessElevation quoting
   fix (+ tests); LauncherService Start try/catch + AutoLaunchTray hoist.
5. **IPC/frontends**: `IpcMethods` table; ControlPlane + IpcDispatcher consume it;
   MCP via ControlPlane; junction anchor helper (fixes MCP drift);
   `BuildDiagnostics.DiagnoseAll` (CLI --diagnose was running half of MCP's
   diagnostics); `DzlJson` consolidation of 5 serializer options + 3 J() helpers.
6. **Tray low-risk**: Workshop browse generation guard (race), `_logCts.Dispose()`
   removal (documented contradiction), LoadGitStatusesAsync generation guard.

## Deferred (separate efforts, tracked here)

- MainViewModel decomposition (WorkshopVm → ToolsVm → ModProjectsVm → LogsVm) —
  each cluster one commit + manual window pass; no UI tests exist.
- RawXmlEditorVm base for the 5 CE editor VMs (~300 duplicated lines: undo/redo,
  Reload, Status report) + parsed-model cache invalidated on write.
- UI-thread sync work: ImportFromGitHub/CreateModProject/DeleteModProject/
  CreateServer → async; status poll Task.Run hop; GitWindow async Refresh.
- TypesService.Set triple-parse fix (probe docs, reuse parsed doc; Parse overload
  stamping SourceFile).
- OpResult unification (TypesOp/RepoOp/WorkshopOp + 2 tuple spellings → OpResult).
- BuildService.Build 170-line method → phase extraction.
- Settings page → SettingsVm two-way bindings.
- Converter consolidation (BoolToBrush, BrushUtil.Freeze ×4).
- Enum-as-string in ConfigStore.Json (visible output change — needs intent).
- CLI Program.cs split into Commands/*.cs + CliOut helpers.

## Constraints honored

Dzl.Core keeps zero project deps; pure helpers split from I/O; wrappers never throw;
ArgvBuilder & tool arg-builders' public behavior frozen (heavily tested); MCP stdout
is protocol (verified clean); presets save to `savePath` not base config.

## Verification

`dotnet build` per project (tray may hold the solution lock; MCP Release exe runs
under Claude — Debug builds only here), `dotnet test tests/Dzl.Core.Tests` after
every phase. New tests: ProcRunner (echo/timeout/never-throw), ProcessElevation
quoting, StateFile IOException, junction-anchor helper, DiagnoseAll.
