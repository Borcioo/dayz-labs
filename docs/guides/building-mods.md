# Building mods

dzl builds a mod source project into a PBO with Addon Builder, but wraps that single call in a
validating pipeline: a **preflight gate** before the build, optional binarization and signing,
a **content-hash build cache** that skips unchanged mods, and verification after.

## Mod projects

A mod project is a source folder under your configured `ProjectsRoot`. You can create, import,
or link one:

```powershell
dotnet run --project src/Dzl.Cli -- new MyMod          # scaffold a new mod source project
dotnet run --project src/Dzl.Cli -- import <path>      # import an existing source folder
dotnet run --project src/Dzl.Cli -- link MyMod         # create/repair the P:\ junction
```

Linking creates a junction on **P:** so the DayZ Tools see your source where they expect it.
`new` and `import` set this up for you.

## Preflight

Run preflight before building to catch the common "why is my mod broken" problems before they
reach Addon Builder:

```powershell
dotnet run --project src/Dzl.Cli -- preflight MyMod
dotnet run --project src/Dzl.Cli -- preflight MyMod --json   # full report as JSON
```

Preflight validates:

- **Config sanity** — `CfgPatches` / `CfgMods` structure, plus a CfgConvert syntax gate that
  front-runs the most common config crash.
- **Asset references** — quoted paths to `.paa` / `.rvmat` / `.p3d` / sounds / etc. that are
  missing, or that resolve on disk but are excluded from the PBO.
- **Baked absolute paths** — a `P:\...` or other drive-absolute path in `model=`, `texture=`,
  `hiddenSelectionsTextures[]`, or an rvmat `texture=`. These resolve on your dev box (P:
  mounted) and break on every other machine — the nastiest ship-it bug, because local testing
  passes.
- **Path hygiene** — the lowercase rule (uppercase paths break DayZ Linux servers), case-only
  duplicate paths, invalid/over-long paths.
- **Texture freshness** — a `.png` / `.tga` source next to an older or missing `.paa` (you
  edited the texture and forgot to convert it).
- **ODOL p3ds** — a `.p3d` that's already binarized (feeding it to Binarize crashes it).
- **Enforce-script traps** — e.g. a base clause on a `modded class` (a silent no-op).

Each finding carries a severity, a rule id, a message, and a file + line. Error-severity
findings block the build (configurable); warnings don't.

## Signing keys

A signing key proves your PBOs weren't tampered with; servers that verify signatures reject
unsigned or mismatched PBOs. Create your creator key pair once — **one key signs all your
mods**:

```powershell
dotnet run --project src/Dzl.Cli -- key new <name>
```

The name defaults to your configured signing key / author. The key is created with the DayZ
Tools `DSCreateKey` and stored in your keys folder.

## Build

```powershell
dotnet run --project src/Dzl.Cli -- build MyMod
```

`build` resolves the project under `ProjectsRoot`, ensures the P: junction, packs the PBO,
deploys it to `P:\Mods\@<Mod>\Addons`, and registers `@<Mod>` in the active server's run-list.

Options:

| Flag | Effect |
|---|---|
| `--clean` | Wipe the output first (Addon Builder `-clear`). |
| `--no-binarize` | Pack only, don't binarize (Addon Builder `-packonly`). |
| `--sign` | Sign the PBO with your signing key (create one with `dzl key new` first). |
| `--key <key>` | Sign with a specific key from the keys folder (default: the configured key). |
| `--force` | Rebuild even when nothing changed (ignore the skip-unchanged cache). |

## The build cache (skip-unchanged)

dzl caches a **content hash** per mod — derived from the packed files' content plus the build
settings that affect output (binarize on/off, sign on/off, key, prefix, tool fingerprints).
On the next build, if the hash matches and the PBO (and signature, if signing) still exists,
the build is skipped.

The hash is content-based on purpose: a git checkout or a `touch` changes file timestamps
without changing content, so a timestamp-only cache would rebuild constantly or, worse, skip
when it shouldn't. Use `--force` to rebuild regardless.

## After a failed build

Addon Builder is a black box — it can fail late, log noisily, and even exit `0` while the log
says "Build failed". dzl treats a fresh PBO appearing as the real success gate, summarizes the
tool output (error / warning / missing-reference counts), and runs the log diagnoser to turn
known patterns into cause → fix entries. See [Central Economy](central-economy.md) and
[MCP](mcp.md) for the related editors and the agent-driven workflow.
