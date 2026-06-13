# Central Economy

dzl edits the **Central Economy (CE)** of the active server mission — the XML that controls
loot spawning, events, and player spawns. It works across all the CE files the mission's
`cfgeconomycore.xml` references, tagging each entry with where it came from
(`vanilla` / `mod` / `custom`).

The tray exposes the editors under **Server → Economy**; the CLI and MCP cover the
`types.xml` workflow.

## Editors

The Economy window has a tab per CE file:

- **Types** — `types.xml`: per-class spawn economy (nominal, min, lifetime, restock, cost,
  quantity min/max, category, usage, value/tiers, tags, flags). Full editing with undo/redo.
- **Dictionaries** — the `cfglimitsdefinition` usage / value / tag / category vocabulary that
  Types is linted against.
- **Random Presets** — `cfgrandompresets.xml`.
- **Spawnable Types** — `cfgspawnabletypes.xml`.
- **Globals** — `globals.xml`.
- **Events** — `events.xml`.
- **Player Spawns** — `cfgplayerspawnpoints.xml`: fresh / hop / travel categories, their
  parameter bags (spawn / generator / group), and named position-group bubbles.

## Linting

The Types editor lints each entry against the dictionary vocabulary and flags structural
problems — unknown `usage` / `value` / `tag` / `category`, duplicate type names, and other
issues. Rows with findings show a warning marker; the panel summarizes the total. From the
CLI:

```powershell
dotnet run --project src/Dzl.Cli -- types lint
```

## Versioned backups

Every CE edit snapshots the file **before** writing. Backups land in a
`.dzl-<stem>-backups\` folder next to the file (e.g. `.dzl-types-backups\` next to
`types.xml`), named `<stem>.<timestamp-id><ext>`. dzl keeps the newest 20 and prunes older
ones. Restoring a backup snapshots the current file first, so a restore is itself undoable.

## CLI: editing types.xml

```powershell
dotnet run --project src/Dzl.Cli -- types ls            # list types (name, nominal, min, lifetime, category, origin, file)
dotnet run --project src/Dzl.Cli -- types lint          # run CE lint rules
dotnet run --project src/Dzl.Cli -- types set <Class>   # set/insert a type (only the given fields change)
dotnet run --project src/Dzl.Cli -- types rm <Class>    # remove a type
dotnet run --project src/Dzl.Cli -- types backups       # list backups (newest first)
dotnet run --project src/Dzl.Cli -- types restore <file>  # restore a backup over the live file
```

`types set` only changes the fields you pass and backs up the file first; `types rm` and
`types restore` back up first too.

## From an AI agent (MCP)

The MCP server exposes `types_list`, `types_lint`, `types_set`, `types_remove`,
`types_backups`, and `types_restore` — the same operations, so an agent like Claude can read,
lint, edit, and roll back the active mission's economy. See [MCP](mcp.md).
