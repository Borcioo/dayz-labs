---
title: MCP server
description: Let an AI agent like Claude drive your installed DayZ Labs launcher — the bundled MCP server exposes dzl's operations as Model Context Protocol tools.
sidebar:
  order: 6
---

DayZ Labs ships with a **Model Context Protocol (MCP)** server. It lets an AI agent —
for example Claude — drive the launcher for you: check status, start and stop the server,
read and diagnose logs, build and preflight mods, edit the Central Economy, download
Workshop items, and run git operations.

This is a power-user feature. You do not need it to use DayZ Labs day to day — the tray app
already does everything through clicks. But if you work with an AI assistant, pointing it at
the MCP server lets it do those same things on your behalf.

## What you need

You don't build anything. When you install DayZ Labs, the MCP server is installed alongside it.
The **MCP** entry in the app's left navigation shows its details and confirms it's present.

The bundled server executable lives at:

```
%LOCALAPPDATA%\DayZLabs\current\mcp\dzl-mcp.exe
```

That's the path you give your MCP client.

## Point your AI agent at it

Add the server to your MCP client's config. For most clients (including Claude) that means a
small JSON entry. Use the full path to the bundled exe:

```json
{
  "mcpServers": {
    "dzl": {
      "command": "%LOCALAPPDATA%\\DayZLabs\\current\\mcp\\dzl-mcp.exe"
    }
  }
}
```

That's it — no `dotnet`, no build step. The server reads the same config the tray app uses, so
the agent sees your real mods, presets, servers, and paths.

By default it operates on your config at:

```
%LOCALAPPDATA%\dzl\config.json
```

If you keep more than one config and want the agent to use a specific one, set the `DZL_CONFIG`
environment variable to that file's absolute path in the same JSON entry under an `"env"` block.

:::note
The server talks to your client over **stdio**, and stdout carries the protocol. The server's
own logging goes to stderr, so don't pipe its stdout anywhere but the MCP client.
:::

## What the agent can do

Once connected, the agent has these tools. They map one-to-one onto the same actions you'd take
in the tray app.

### Lifecycle and status

| Tool | What it does |
|---|---|
| `status` | Running state, mode, port, active profile, paths, enabled mods, newest log files. |
| `start` | Start the server (and optionally the client). `mode` = `debug` \| `normal`. |
| `stop` | Stop the server (and optionally the client). |
| `restart` | Restart the server. `mode` = `debug` \| `normal`. |

### Mods and presets

| Tool | What it does |
|---|---|
| `list_mods` | The enabled mods (path + side) of the active profile. |
| `list_presets` | List profiles/presets; the active one is flagged. |
| `set_preset` | Switch the active profile by name. |
| `list_mod_projects` | Mod source projects under your Projects root with their P: link state. |
| `new_mod` / `import_mod` / `link_mod` | Scaffold / import / link a mod source project. |

### Logs and diagnosis

| Tool | What it does |
|---|---|
| `logs` | Last N lines of a log: `script` \| `rpt` \| `adm` \| `client`. |
| `diagnose_logs` | Scan a log tail for known failure signatures (verification kicks, build-tool symptoms) → cause/fix entries. |

### Build and DayZ Tools

| Tool | What it does |
|---|---|
| `preflight` | Validate a mod before building (config sanity, references, baked paths, path hygiene, ODOL, script traps). |
| `build_mod` | Build a mod into a PBO and add `@<Mod>` to the active server's run-list. |
| `generate_key` | Create your signing key pair (one key signs all your mods). |
| `pack_pbo` | Pack a source folder into a PBO (Addon Builder). |
| `unbinarize` | Unbinarize a `config.bin` to `.cpp` (CfgConvert / DeRap). |
| `convert_paa` | Batch convert PNG/TGA to PAA in a folder (ImageToPAA). |
| `list_tools` / `open_tool` | Discover / launch DayZ Tools GUIs. |
| `work_drive_action` | Check / mount / unmount the P: work drive. |

### Central Economy

| Tool | What it does |
|---|---|
| `types_list` | List types from the active mission's CE files (filter by name / origin / file). |
| `types_lint` | Lint the active mission's CE against `cfglimitsdefinition` and structural rules. |
| `types_set` | Set/insert a type (only given fields change; versioned backup first). |
| `types_remove` | Remove a type (versioned backup first). |
| `types_backups` / `types_restore` | List / restore versioned `types.xml` backups. |

### Server instances

| Tool | What it does |
|---|---|
| `new_server` | Scaffold a new server instance and save it as a preset. |
| `list_servers` | List scaffolded server instances. |
| `use_server` | Activate a server instance by name. |

### Steam Workshop

| Tool | What it does |
|---|---|
| `workshop_search` | Search the Workshop (needs a Steam Web API key). |
| `workshop_add` | Download an item via steamcmd (opens a console for Steam login/Guard). |
| `workshop_update` | Re-download item(s) to update them. |

### git / GitHub

| Tool | What it does |
|---|---|
| `repo_status` | Git status of a mod project. |
| `git_changes` / `git_log` / `git_diff` | Changed files / recent commits / work-tree diff. |
| `git_commit` | Stage and commit a mod's changes. |
| `git_branches` / `git_checkout` / `git_create_branch` | List / check out / create branches. |
| `git_push` / `git_pull` | Push / pull the current branch (needs a remote). |
| `create_repo` | Init git + create & push a GitHub repo for the mod. |
| `release` | Cut a GitHub release at HEAD (creates + pushes the tag). |

## Good to know

- `workshop_add` opens the steamcmd console for an interactive login plus Steam Guard the first
  time — you'll need to complete sign-in yourself.
- Central Economy writes always snapshot the file first, so an agent can't lose your work. Use
  `types_backups` / `types_restore` to roll back.
- If the tray app is open with its optional automation server turned on (it hosts a named pipe,
  `dzl-ipc-v1`), the agent's actions route through that running app so it stays the single source
  of truth — what the agent does, you see live in the tray. This is off by default; turn it on in
  the app only if you want the agent and the tray to share one live session.
