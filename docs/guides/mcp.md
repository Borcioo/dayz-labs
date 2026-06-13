# MCP server

`Dzl.Mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server. It exposes
dzl's operations as MCP tools so an AI agent — for example Claude — can drive the launcher:
check status, start/stop the server, read and diagnose logs, build and preflight mods, edit
the Central Economy, download Workshop items, and run git operations.

## Transport and config

The server speaks over **stdio**. Because **stdout is the protocol stream**, all of the
server's own logging goes to **stderr** — don't pipe stdout anywhere but the MCP client.

It operates on the config named by the `DZL_CONFIG` environment variable, falling back to the
default location:

```
%LOCALAPPDATA%\dzl\config.json
```

Set `DZL_CONFIG` to an absolute path to point the agent at a specific config.

## Setup snippet

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

You can also point `command` at a built `Dzl.Mcp` executable instead of `dotnet run`.

## Tool catalog

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
| `list_mod_projects` | Mod source projects under ProjectsRoot with their P: link state. |
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

## Notes for agents

- `workshop_add` opens the steamcmd console for an interactive login + Steam Guard the first
  time — a human needs to complete sign-in.
- Central Economy writes always snapshot the file first; use `types_backups` / `types_restore`
  to roll back.
- If a tray is running with its automation pipe enabled, routed operations go through that
  live process so it stays the single source of truth.
