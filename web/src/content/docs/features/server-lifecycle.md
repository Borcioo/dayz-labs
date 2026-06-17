---
title: Server & client lifecycle
description: Start, stop, and restart your DayZ dev server and client from the DayZ Labs Dashboard.
sidebar:
  order: 1
---

The core loop of mod testing is "launch the server, launch the client, watch it, kill it,
try again." DayZ Labs turns that into a few clicks on the **Dashboard** — no more
hand-built command lines.

Everything on this page happens in the installed tray app. If you haven't set it up yet,
start with [Install & first run](/dayz-labs/start/install/).

## The Dashboard

The Dashboard is the first thing you see when you open DayZ Labs. It has two cards — a
**Server** card and a **Client** card — and a row of status pills along the top
(SERVER / CLIENT / MCP / P: / GITHUB / STEAM) that tell you at a glance what's running and
what's connected.

![The DayZ Labs dashboard — server & client lifecycle](../../../assets/screens/dashboard.png)

*The Dashboard: Server and Client cards, each with Start / Stop / Restart and a live launch-command preview.*

Each card has the same three buttons — **Start**, **Stop**, and **Restart** — plus:

- a **live preview** of the exact command line DayZ Labs will run, so you can see precisely
  what's about to launch before you click;
- the **active mods** list, in load order, so you know what's going into this session;
- **Edit mods** and **Edit params** buttons to change the loadout or launch parameters
  without leaving the Dashboard.

## Debug or normal mode

At the top of the Dashboard is a **Mode** toggle:

- **Debug** launches DayZ's `DayZDiag` executable. This is the one you want while developing
  — it writes the script log, so you can see your `Print()` output and catch script errors.
- **Normal** launches the regular release build, for checking how your mod behaves the way
  players will actually run it.

Flip the toggle and the launch-command preview on each card updates instantly to match.

## Picking which server runs

The **Server** dropdown at the top of the Dashboard chooses which of your server instances
is active. Each instance is its own `serverDZ.cfg`, profile, port, and mod loadout, so you
can keep, say, a Chernarus test server and a Livonia test server side by side and switch
between them in one click. You create and manage these on the
[Servers](/dayz-labs/guides/server-instances/) page.

## Start, stop, restart

- **Start** spins up the server (or client) in the mode you've selected, with the active
  mods and params shown on the card.
- **Restart** stops and relaunches in one step — the button you'll reach for most after a
  code change.
- **Stop** shuts it down cleanly when you're done.

You can run the server on its own, the client on its own, or both — each card is
independent.

## The status you see is the truth

DayZ Labs remembers the process IDs it spawned and checks them against the actually-running
process image. That means a recycled PID is never mistaken for a live server: when the
SERVER or CLIENT pill says it's up, it really is. If a process dies on its own, the pill
flips back and the card's buttons return to their ready state.

## Watching the logs

Once the server is up, head to the [Logs](/dayz-labs/features/logs-and-diagnosis/) page for a live tail of
the Script, RPT, ADM, and Client logs — so you can watch your mod load and spot errors
without hunting for log files on disk.

## Power users: CLI & automation

The Dashboard is the main way to drive the lifecycle, but the same actions are available
from the bundled command-line tool and the [MCP server](/dayz-labs/guides/mcp/) if you
want to script your loop or let an AI agent run it. For example, from the CLI:

```powershell
dzl start --client       # server + client, debug mode
dzl restart
dzl stop --client
```

These are optional extras — you never need them for everyday testing.
