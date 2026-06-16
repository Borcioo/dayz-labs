---
title: Server & client lifecycle
description: Start, stop, and restart the DayZ dev server and client.
sidebar:
  order: 1
---

The core loop of mod testing is "launch the server, launch the client, watch it, kill it,
try again." dzl turns that into single actions you can run from the tray, the CLI, or an AI
agent — no more hand-built command lines.

You start the dev server and client in **debug** mode (DayZ's `DayZDiag` executable, for
script logging) or **normal** release mode. dzl remembers the process IDs it spawned and
checks them against the running process image, so a recycled PID is never mistaken for a live
server — the status you see is the truth.

## What you can do

- Start the server, the client, or both — in debug or release mode.
- Restart the whole session after a code change in one step.
- Stop cleanly when you're done.
- Dry-run a launch to see the exact command line before anything spawns.

From the CLI it's as short as:

```powershell
dzl start --client       # server + client, debug mode
dzl restart
dzl stop --client
```
