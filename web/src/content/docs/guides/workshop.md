---
title: Steam Workshop
description: Search the Steam Workshop and download or update DayZ mods through steamcmd.
sidebar:
  order: 5
---

dzl can search the Steam Workshop and download/update DayZ mods through **steamcmd**.

## What you need

- **Search** needs a **Steam Web API key** in your config.
- **Download / update** needs **steamcmd** configured.

## Search

```powershell
dotnet run --project src/Dzl.Cli -- workshop search "<query>"
```

Search uses the Steam Web API and returns matching items (id + title). Without a Web API key,
search is unavailable — download still works.

## Download and update

```powershell
dotnet run --project src/Dzl.Cli -- workshop add <id>        # download a Workshop item by published-file id
dotnet run --project src/Dzl.Cli -- workshop update <id>     # re-download an item to update it
dotnet run --project src/Dzl.Cli -- workshop update          # update all downloaded items
```

### The visible login console

`workshop add` / `workshop update` shell out to **steamcmd**, which **opens a console window
for the Steam login**. This is intentional and necessary: steamcmd handles your Steam
username/password and **Steam Guard** prompt interactively in that console. Watch the console,
enter your credentials, and approve the Steam Guard challenge there — the download proceeds
once you're authenticated. steamcmd caches the session, so subsequent downloads usually don't
prompt again.

## A note on the two Steam paths

dzl has a second, separate Steam integration used by the tray to **subscribe** to Workshop
items via SteamKit2 (the "Sign in to Steam" window, with a Steam Guard mobile approval). That
is distinct from the **steamcmd download** path described above. For command-line and
agent-driven downloads, the steamcmd flow is the one in play — and that's the one with the
visible login console.

## From an AI agent (MCP)

The MCP server exposes `workshop_search`, `workshop_add`, and `workshop_update` — the same
operations. Note that `workshop_add` still opens the steamcmd console for the interactive
login/Guard step, so a human needs to complete sign-in the first time. See [MCP](/dayz-labs/guides/mcp/).
