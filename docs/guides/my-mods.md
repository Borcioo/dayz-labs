# My Mods

A mod-development hub for DayZ. Your sources and builds live in **normal project folders**
(git-friendly, outside P:); dzl drops filesystem **junctions** into P: so the DayZ Tools see them
where they expect, and wraps Addon Builder in a validating build → deploy pipeline.

## Layout on disk

Everything physical lives under one configurable `ProjectsRoot` (default
`%USERPROFILE%\DayZProjects`). P: holds **only junctions** pointing back into it.

```
<ProjectsRoot>\
├── mods\<Mod>\          source project (+ .dzl\ metadata)  ──┐
├── build\@<Mod>\        build output (Addons\*.pbo, keys\)  ─┼─ surfaced on P: via junctions
├── servers\<name>\      server instances                     │
├── keys\                signing keys (one pair, all mods)     │
└── workshop\            steamcmd downloads                    │
                                                               │
P:\<Mod>          ──junction──>  <ProjectsRoot>\mods\<Mod>   ◄─┘  (Addon Builder reads source here)
P:\Mods\@<Mod>    ──junction──>  <ProjectsRoot>\build\@<Mod>      (engine loads built PBO here)
```

A folder counts as a mod project if it contains `$PBOPREFIX$` **or** `config.cpp`. The junction is
anchored on the **work-drive source folder** (the always-live folder P: is mounted from), so its
state stays correct and repairable even when P: is unmounted.

## Creating / adding a project

Three entry points, all end the same way: source in `mods\<Mod>\` + a `P:\<Mod>` junction, then the
list refreshes.

```
┌─ New (scaffold) ──────┐   ┌─ Import local folder ─┐   ┌─ Import from GitHub ──┐
│ ModScaffold.Scaffold  │   │ ModImport.Import      │   │ gh clone → mods\<Mod> │
│  ($PBOPREFIX$,        │   │  (non-invasive: links │   │  3 git modes:         │
│   config.cpp, gproj)  │   │   source in place,    │   │  • Clone   keep .git  │
│ + cache author        │   │   never copies/moves) │   │  • Snapshot strip .git│
│ + optional git init   │   │                       │   │  • Fresh   new repo   │
└───────────┬───────────┘   └───────────┬───────────┘   └───────────┬───────────┘
            └───────────────────────────┴───────────────────────────┘
                                        ▼
                       Junction.Ensure( P:\<Mod> → mods\<Mod> )
                                        ▼
                              shows up as a card on My Mods
```

- **Import local** links the external folder into `mods\<Mod>` *and* P: — the source is never moved
  or duplicated. UNC/network paths are refused (junctions can't target them).
- **GitHub Clone** keeps the upstream `.git` (your own repo); **Snapshot** strips it (you just want
  the files); **Fresh** strips it and starts a new local repo with an initial commit (use a sample
  as the starting point of *your* mod).
- Each card shows a live **git badge** (branch • dirty/clean • ahead/behind • `(local)` if no
  remote), filled off the UI thread.

## Building

`build` is a single Addon Builder call wrapped in a gated, verifying, atomic pipeline:

```
build <Mod>
  │
  1. Validate env ........ is a project? AddonBuilder found? P: mounted?       ─ fail fast
  2. Preflight gate ...... configs, asset refs, baked P:\ paths, lowercase,    ─ errors BLOCK
  │                        stale .paa, ODOL p3ds, modded-class traps             (opt-out via flag)
  3. Resolve sign key .... checked BEFORE the slow build, not after            ─ fail fast
  4. Cache skip .......... content-hash(payload) + settings fingerprint        ─ skip if unchanged
  │                        matches & PBO still present → done                    (--force overrides)
  5. Ensure junctions .... source (P:\<Mod>) + build area (P:\Mods)
  6. Pack ................ AddonBuilder → build\@<Mod>\.work\Addons  (NOT the loadable dir yet)
  7. Verify .............. fresh .pbo exists? prefix == $PBOPREFIX$? .bisign present if signing?
  │                        (AddonBuilder lies — it reports "Build Successful" on no-ops/mangles)
  8. Publish atomically .. backup loadable Addons → swap in new → rollback on any failure
  9. Record .............. ownership marker + content-hash cache
 10. Ship public key ..... copy .bikey into build\@<Mod>\keys\ (outside the PBO) — if signing
 11. Register ............ add P:\Mods\@<Mod> to the active server's run-list (deduped)
```

Why each guard exists:

- **Preflight gate** — Addon Builder reports success even for configs it silently mangles; preflight
  catches the classic ship-it bugs first (notably baked `P:\…` absolute paths that pass on your box
  and break on every other machine).
- **Pack to `.work\` then atomic publish** — a failed rebuild never leaves a half-written
  `@<Mod>\Addons` that the server might be loading.
- **Verify (fresh PBO + prefix + signature)** — the real success gate is a *fresh* PBO appearing
  with the right prefix, not Addon Builder's exit code.
- **Content-hash cache** — git checkouts / `touch` change timestamps but not content, so the cache
  is content-based; unchanged mods are skipped, `--force` rebuilds anyway.

Build options: `--clean` (wipe first), `--no-binarize` (pack only), `--sign` (+`--key <name>`),
`--force`. One signing key signs all your mods and is **never overwritten** (a lost
`.biprivatekey` means none of those mods can be updated again).

## Other per-project actions

| Action | What it does |
|---|---|
| **Link / Unlink** | (Re)create or remove the `P:\<Mod>` junction. Unlink leaves the source untouched. |
| **Delete** | Remove the junction (link only, never the target), delete the source folder, optionally the build output. |
| **Publish to GitHub** | `git init` + commit + `gh repo create` from the per-mod Git window. |
| **Release** | Cut a GitHub release, optionally attaching the built PBO as an asset. |

## The one idea to take away

> Your mod source never has to live inside P:. It lives in a normal, git-versioned project folder; a
> junction surfaces it at `P:\<Mod>` for the toolchain. You build straight from versioned source — no
> copying in and out of P: — and dzl drives the work drive, preflight, signing, caching, and atomic
> deploy around that single Addon Builder call.

See also [building-mods.md](building-mods.md) for the build pipeline in depth.
