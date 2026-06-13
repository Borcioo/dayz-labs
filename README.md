# dzl

`dzl` is a DayZ mod-development launcher for Windows. It starts and stops the DayZ dev
server and client, manages an ordered mod selection plus named config presets, tails the
DayZ logs (script / rpt / adm / client) and diagnoses common failures, and wraps the DayZ
Tools binaries — Addon Builder, ImageToPAA, CfgConvert, the **P:** work drive, and more. On
top of that it adds a validating build pipeline, Central Economy editors, Steam Workshop
downloads, and git/GitHub integration. The same engine ships behind three frontends: a CLI,
an MCP server (so an AI agent like Claude can drive it), and a WPF system-tray app.

> Screenshot placeholder — add a tray screenshot here (`docs/images/tray.png`).

## Features

- **Server / client lifecycle** — start, stop, restart the dev server and client in `debug`
  (DayZDiag) or `normal` (release) mode. Spawned PIDs are tracked so a recycled PID is never
  mistaken for a live server.
- **Ordered mods + named presets** — an ordered mod selection where each mod is tagged
  `both` / `server` / `client` (server-only mods go to `-serverMod`, the rest to `-mod`).
  Full config snapshots are saved as named presets you can switch between.
- **Log tailing + diagnosis** — read the last N lines of any DayZ log, and run a diagnoser
  that pattern-matches known failure signatures (verification kicks such as
  `VE_MISSING_BISIGN` / `VE_PATCHED_PBO`, mod version skew, build-tool symptoms) into
  cause → fix entries.
- **DayZ Tools wrappers** — discover and launch the DayZ Tools GUIs, batch-convert textures
  to PAA, pack folders into PBOs, and unbinarize configs. Mount / unmount the **P:** work
  drive without opening the Tools GUI.
- **Build pipeline** — build a mod project into a PBO with a preflight gate (config sanity,
  missing/excluded asset references, baked absolute paths, path-hygiene rules, Enforce-script
  traps), optional binarization and signing, a content-hash build cache that skips unchanged
  mods, and post-build verification.
- **Signing keys** — create your creator key pair once; one key signs all your mods.
- **Central Economy editors** — edit a mission's `types.xml` and the rest of the CE config
  (events, globals, spawnable types, random presets, player spawns) with linting and
  versioned backups.
- **Steam Workshop** — search the Workshop (Steam Web API) and download/update items via
  steamcmd.
- **git / GitHub integration** — treat a mod project as a git repo: status, changes, log,
  diff, commit, branches, push/pull, publish a new GitHub repo, and cut releases.
- **Three frontends, one core** — a CLI, an MCP server for AI agents, and a WPF tray app, all
  over the shared `Dzl.Core` engine.

## Requirements

- Windows.
- **DayZ** and **DayZ Tools**, both installed via Steam. (DayZ Tools provides Addon Builder,
  ImageToPAA, CfgConvert, the WorkDrive, etc.)
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** to build and run.
- Optional, for specific features: **steamcmd** (Workshop downloads), the **GitHub CLI**
  (`gh`, for publishing repos and releases), and a **Steam Web API key** (Workshop search).

## Quick start

### Build

```powershell
dotnet build Dzl.sln -c Debug
```

### Run the tray

```powershell
.\run-dzl.bat
```

`run-dzl.bat` builds and launches the tray app **de-elevated**, via `explorer.exe`. This
matters: the tray mounts the **P:** work drive, and the P: drive is only visible inside the
session that mounted it. If the tray runs elevated it mounts P: in the *admin* session, where
the (non-admin) game and Explorer can't see it. Launching through `explorer.exe` drops the
process back to the normal user session so P: is visible everywhere. Prefer this script over
`dotnet run` for the tray.

On first run with no config, the tray opens a **setup wizard** that detects DayZ + DayZ Tools,
mounts P:, helps extract vanilla game data, scaffolds a server instance, and sets your mod
scan-roots.

### A short CLI tour

```powershell
# build the CLI once, then run the dll, or use `dotnet run --project src/Dzl.Cli -- <args>`
dotnet run --project src/Dzl.Cli -- status            # launcher status
dotnet run --project src/Dzl.Cli -- mods              # the ordered mod selection
dotnet run --project src/Dzl.Cli -- start --client    # start server + client (debug mode)
dotnet run --project src/Dzl.Cli -- start --dry-run   # print the argv, don't spawn
dotnet run --project src/Dzl.Cli -- logs script --lines 100
dotnet run --project src/Dzl.Cli -- stop --client
dotnet run --project src/Dzl.Cli -- preset load hardcore   # switch active preset

dotnet run --project src/Dzl.Cli -- preflight MyMod   # validate before building
dotnet run --project src/Dzl.Cli -- build MyMod --sign # build + sign + add to the run-list
```

Run `dotnet run --project src/Dzl.Cli -- --help` for the full command list, and
`<command> --help` for any subcommand.

### Drive it from Claude (MCP)

`Dzl.Mcp` is an MCP server that speaks over **stdio** — so an MCP client such as Claude can
call the same operations the CLI does (status, start/stop, logs, build, preflight, Central
Economy edits, Workshop, git, …). Point the `DZL_CONFIG` env var at the config you want it to
operate on.

```json
{
  "mcpServers": {
    "dzl": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/path/to/dzl-dotnet/src/Dzl.Mcp"],
      "env": { "DZL_CONFIG": "%LOCALAPPDATA%/dzl/config.json" }
    }
  }
}
```

stdout is the MCP protocol stream, so the server logs only to stderr. See
[docs/guides/mcp.md](docs/guides/mcp.md) for the tool catalog.

## Documentation

User guides live in [`docs/guides/`](docs/guides/):

- [Getting started](docs/guides/getting-started.md)
- [Building mods](docs/guides/building-mods.md)
- [Central Economy](docs/guides/central-economy.md)
- [Steam Workshop](docs/guides/workshop.md)
- [Server instances](docs/guides/server-instances.md)
- [MCP server](docs/guides/mcp.md)

Internal design notes for past and upcoming phases live in [`docs/plans/`](docs/plans/).

## License

TODO — the project owner needs to choose a license before publishing.
