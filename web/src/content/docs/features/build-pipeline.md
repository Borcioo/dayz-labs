---
title: Build pipeline
description: One-click mod builds in My Mods — guarded, verified, and safe to ship.
sidebar:
  order: 5
---

Packing a DayZ mod really comes down to one Addon Builder call — but Addon Builder is a
black box. It can quietly mangle a config, report "success" on a build that did nothing, and
leave you shipping a broken PBO. dzl wraps that single call in a pipeline that checks your
work before, during, and after the build, so the PBO that lands is one you can trust.

You don't run any of this from a command line. It's a button. In the tray app, open the
**My Mods** page and each of your source projects gets its own row with a one-click **Build**.

![The My Mods page — your source projects with one-click Build](../../../assets/screens/mymods.png)

*Each source mod under your Projects root shows up here with a Build button, git actions, and an open-folder button.*

## What happens when you click Build

A **preflight gate** runs first and catches the classic ship-it bugs — the worst being a
baked `P:\...` absolute path that works on your machine and breaks on everyone else's. It also
flags config problems, missing asset references, and path-hygiene issues before they reach the
game.

Then dzl builds, with optional binarization and signing. When it finishes, it doesn't take
Addon Builder's word for it — it verifies that a fresh PBO actually appeared with the right
prefix and signature, then swaps it into place atomically. A failed rebuild never leaves a
half-written mod for the server to pick up.

## What you can do

- Build any source project straight from My Mods with one click — no terminal, no scripts.
- Let preflight catch config, asset-reference, baked-path, and path-hygiene problems early.
- Build from your versioned source, with optional binarization and signing.
- Skip unchanged mods automatically thanks to a content-hash cache, so rebuilds are fast.
- Trust the "build succeeded" message — it's checked against the real output, not just the
  tool's exit code.

The built output lands under your Projects root (in `build/`), ready to load into a server
instance from the Dashboard.

## Power users and automation

If you'd rather script your builds — or let an AI agent drive them through the bundled MCP
server — the same verified pipeline is available from the CLI:

```powershell
dzl preflight MyMod
dzl build MyMod --sign
```

[Go deeper →](/dayz-labs/guides/building-mods/)
