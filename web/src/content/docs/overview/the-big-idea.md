---
title: The big idea
description: Source outside P:, junctions surface it, one core drives everything.
---

Two ideas explain most of how dzl is put together.

## 1. Your mod source never lives inside P:

DayZ Tools expect a mounted **P:** work drive. dzl keeps your actual source in a normal,
git-versioned project folder **outside** P:, then drops filesystem **junctions** into P: so
the tools see it where they expect. You build straight from versioned source — no copying in
and out of P: — and dzl drives the work drive, preflight, signing, caching, and atomic
deploy around a single Addon Builder call.

## 2. One core, three frontends

Every capability lives in one engine. The CLI, the MCP server, and the tray app are thin
shells over it, so they behave identically. Whatever you can do from the tray, you can script
from the CLI or hand to an AI agent over MCP.
