---
title: What is dzl
description: A DayZ mod-development launcher for Windows.
---

dzl is a launcher and toolkit for **developing DayZ mods on Windows**. It runs your local
dev server and client, keeps an ordered list of the mods you're testing, reads and explains
the game's logs, and wraps the DayZ Tools so you don't have to drive them by hand. On top of
that it adds a validating build pipeline, Central Economy editors, Steam Workshop downloads,
and git/GitHub integration.

The same engine ships three ways: a command-line tool (CLI), an MCP server (so an AI agent
like Claude can drive it), and a Windows system-tray app. Pick whichever fits how you work —
they all do the same thing.

## What you get

- Start / stop / restart the dev server and client, in debug or release mode.
- An ordered mod selection saved as named, switchable presets.
- Live log tailing with a diagnoser that turns known failures into "cause → fix".
- A guarded build → sign → deploy pipeline around DayZ's Addon Builder.
- Editors for the mission's Central Economy (loot, events, spawns) with linting and backups.
- Steam Workshop search + download, and git/GitHub actions per mod project.

Ready to try it? Head to [Getting started](/dayz-labs/guides/getting-started/).
