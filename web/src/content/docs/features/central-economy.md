---
title: Central Economy editors
description: Edit the mission's loot economy with linting and versioned backups.
sidebar:
  order: 6
---

A DayZ server's loot, events, and player spawns are controlled by a set of XML files — the
**Central Economy** — and hand-editing them is error-prone: one unknown category or typo'd tag
and the mission silently misbehaves. dzl gives you editors for those files that understand the
format, check your work as you go, and keep a safety net of backups.

It works across every CE file your mission references, and labels each entry by where it came
from — vanilla, a mod, or your own custom value — so you always know what you're changing. The
Types editor lints each entry against the mission's own vocabulary and flags unknown usages,
duplicate names, and structural problems before they reach the game.

## What you can do

- Edit `types.xml` and the rest of the CE (events, globals, spawnable types, random presets,
  player spawns) from the tray.
- Lint entries against the mission's `cfglimitsdefinition` vocabulary to catch typos and bad
  values.
- Roll back safely — every edit snapshots the file first and keeps the most recent backups.
- List, edit, lint, and restore `types.xml` from the CLI or an AI agent too.

From the CLI:

```powershell
dzl types lint
dzl types set <Class>
```

[Go deeper →](/dayz-labs/guides/central-economy/)
