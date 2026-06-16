---
title: Getting started
description: From a fresh checkout to a running local DayZ dev server.
sidebar:
  order: 1
---

This guide takes you from a fresh checkout to a running local DayZ dev server.

## Requirements

- Windows.
- **DayZ** and **DayZ Tools**, both installed via Steam.
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**.

## Build

```powershell
dotnet build Dzl.sln -c Debug
```

## Run the tray (recommended for first-time setup)

```powershell
.\run-dzl.bat
```

`run-dzl.bat` builds and launches the tray **de-elevated** through `explorer.exe`. This is
deliberate. The tray mounts the **P:** work drive, and a mounted drive is only visible inside
the session that mounted it. An elevated tray would mount P: in the admin session, where the
(non-admin) game and Explorer can't see it — so the script bounces the launch through
`explorer.exe` to land in your normal user session. Prefer this over `dotnet run` for the tray.

### First-run setup wizard

The first time you launch the tray with no config, a setup wizard runs. It walks you through:

1. Detecting and confirming your DayZ and DayZ Tools install paths.
2. Mounting the **P:** work drive.
3. Extracting vanilla game data (via DayZ Tools).
4. Scaffolding a server instance (`serverDZ.cfg`, profile folders, a mission copy).
5. Setting your mod scan-roots.

You can re-run it later from the tray's **Setup** / **Run setup wizard…** entry.

## The tray at a glance

The tray's left navigation groups the features:

- **General** — Dashboard (status, start/stop/restart, mode + port + active profile),
  Mods, My Mods (your source projects), Tools.
- **Server** — Servers (instances/presets), Bases (templates), Economy (Central Economy
  editors), Logs (live tailing of script / rpt / adm / client).
- **Setup / MCP / Settings** — re-run the wizard, MCP info, and configuration.

## Config

Configuration lives at:

```
%LOCALAPPDATA%\dzl\config.json
```

You can point any frontend at a different file:

- Environment: set `DZL_CONFIG` to an absolute path.
- CLI: pass `--config <path>` to any command.

The JSON is snake_case. Presets are full config snapshots stored in `presets/*.json` next to
the config; the base config's `active_preset` field names the live one. Print the resolved
path any time with:

```powershell
dotnet run --project src/Dzl.Cli -- config path
```

## CLI basics

```powershell
dotnet run --project src/Dzl.Cli -- status                 # running state, mode, port, profile, logs
dotnet run --project src/Dzl.Cli -- mods                   # the ordered mod selection
dotnet run --project src/Dzl.Cli -- start --client         # server + client, debug mode (default)
dotnet run --project src/Dzl.Cli -- start --normal         # release mode
dotnet run --project src/Dzl.Cli -- start --dry-run        # print argv, don't spawn
dotnet run --project src/Dzl.Cli -- restart
dotnet run --project src/Dzl.Cli -- stop --client
dotnet run --project src/Dzl.Cli -- logs script --lines 100
```

`debug` mode launches `DayZDiag_x64.exe`; `normal` mode launches the release executable.

Run `--help` on any command for its full option list.

## The P: work drive

DayZ Tools uses a mounted **P:** drive for vanilla game data and as a packing root. dzl can
mount/check/unmount it without opening the Tools GUI:

```powershell
dotnet run --project src/Dzl.Cli -- workdrive status
dotnet run --project src/Dzl.Cli -- workdrive mount
dotnet run --project src/Dzl.Cli -- workdrive unmount
```

The drive must be mounted **de-elevated**, in your user session, or it lands in an invisible
session the game can't read — which is exactly why the tray is launched the way it is (see
above).

## Next steps

- [Building mods](/dzl/guides/building-mods/)
- [Central Economy](/dzl/guides/central-economy/)
- [Server instances](/dzl/guides/server-instances/)
- [Steam Workshop](/dzl/guides/workshop/)
- [MCP server](/dzl/guides/mcp/)
