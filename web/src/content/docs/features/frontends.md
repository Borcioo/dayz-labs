---
title: Three frontends, one core
description: A CLI, an MCP server, and a tray app over one shared engine.
sidebar:
  order: 9
---

Everything dzl can do lives in a single engine, and three different frontends sit on top of
it. Because they're all thin shells over the same core, they behave identically — so you pick
whichever fits the moment instead of learning three different tools.

The **CLI** is great for scripting and quick one-off commands. The **tray app** is a full
Windows UI with a setup wizard, dashboards, and the Central Economy editors. And the **MCP
server** lets an AI agent like Claude drive the launcher directly — checking status, starting
the server, reading and diagnosing logs, building mods, editing the economy, and more.

## What you can do

- Script your build-and-test loop from the command line.
- Work visually in the tray app, with a guided first-run setup wizard.
- Hand the whole launcher to an AI agent over MCP and let it run your iteration loop.
- Mix and match — whatever you do in one frontend is reflected in the others, since they share
  one core and live state.

The MCP server speaks over stdio, so any MCP client can connect to it.

[Go deeper →](/dayz-labs/guides/mcp/)
