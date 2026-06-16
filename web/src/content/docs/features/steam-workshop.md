---
title: Steam Workshop
description: Search the Workshop and download or update mods via steamcmd.
sidebar:
  order: 7
---

Testing against other people's mods means pulling them off the Steam Workshop — and dzl can do
that for you, both finding items and downloading them. Search the Workshop by name, then
download or update any item by its id, all without leaving your usual workflow.

Downloads go through **steamcmd**, which opens its own console window for the Steam login and
Steam Guard prompt. That's deliberate: steamcmd handles your credentials directly in that
console, you approve the Guard challenge once, and it remembers the session so later downloads
usually don't ask again.

## What you can do

- Search the Workshop by name to find an item's id (needs a Steam Web API key).
- Download a Workshop item by id, ready to add to your run-list.
- Update a single item, or update everything you've downloaded, in one command.
- Sign in once per session in the steamcmd console — including the Steam Guard step.

From the CLI:

```powershell
dzl workshop search "trader"
dzl workshop add <id>
```

[Go deeper →](/dzl/guides/workshop/)
