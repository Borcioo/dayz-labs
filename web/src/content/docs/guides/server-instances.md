---
title: Server instances
description: Self-contained server configurations saved as named presets you can switch between.
sidebar:
  order: 4
---

A **server instance** is a self-contained server configuration (its own `serverDZ.cfg`,
profile folders, mission, port, and mod run-list). dzl stores each instance as a named
**preset** — a full config snapshot — and the active preset is the one that launches.

## Presets

A preset is a complete config snapshot saved in `presets/*.json` next to your config. The base
config's `active_preset` field names the live one. Switching presets switches everything: which
server instance, which mods, which params.

```powershell
dotnet run --project src/Dzl.Cli -- preset save <name>   # save current config as a preset and activate it
dotnet run --project src/Dzl.Cli -- preset load <name>   # make a preset active
dotnet run --project src/Dzl.Cli -- preset rm <name>     # delete a preset
```

## Creating and switching instances

```powershell
dotnet run --project src/Dzl.Cli -- server new <name>    # scaffold a new server instance
dotnet run --project src/Dzl.Cli -- server ls            # list instances
dotnet run --project src/Dzl.Cli -- server use <name>    # activate an instance (switches the active preset)
dotnet run --project src/Dzl.Cli -- server rm <name>     # remove an instance (keeps its files unless --purge)
```

`server new` scaffolds the instance and saves it as a preset; `server use` activates it by
switching the active preset. Creating an instance lets you pick the map (e.g. `chernarus`,
`livonia`) and a UDP port (auto-assigned if you don't pick one).

## Maps and multi-server

Because each instance is its own preset, you can keep several side by side — for example one
per map, or hardcore vs. casual variants of the same map — and switch between them with
`server use` (or `preset load`). Each carries its own mods, params, and port, so they don't
collide.

## Bases (templates)

A **base** is a template that new instances can be created from — so you don't re-create the
same `serverDZ.cfg` / mission setup every time.

```powershell
dotnet run --project src/Dzl.Cli -- base ls              # list bases
dotnet run --project src/Dzl.Cli -- base new <name>      # create a base (from the DayZ install by default, or --empty)
dotnet run --project src/Dzl.Cli -- base rm <name>       # delete a base
```

`base new` builds the template from your DayZ install by default; pass `--empty` for a blank
one.

## From the tray

The tray surfaces all of this under **Server → Servers** (instances) and **Server → Bases**
(templates). The Dashboard shows the active profile, mode, and port, with start / stop /
restart.

## From an AI agent (MCP)

The MCP server exposes `new_server`, `list_servers`, and `use_server` (plus `list_presets` /
`set_preset`), so an agent can scaffold and switch instances. See [MCP](/dzl/guides/mcp/).
