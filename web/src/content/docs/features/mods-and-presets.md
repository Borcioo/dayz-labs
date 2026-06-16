---
title: Mods & presets
description: An ordered mod selection saved as named, switchable config presets.
sidebar:
  order: 2
---

Testing a mod usually means running it alongside a handful of others, in a particular order,
against a particular server setup. dzl keeps that as an **ordered list of mods** and saves the
whole configuration as a named **preset** you can switch to in one move — so flipping between
"vanilla", "my mod only", and "full modpack" is instant instead of fiddly.

Each mod in the list is tagged **both**, **server**, or **client**. dzl uses that tag to put
server-only mods on the launch line as `-serverMod` and the rest as `-mod`, so the right
content loads on the right side without you tracking it by hand.

## What you can do

- Build an ordered list of the mods you're testing and reorder it freely.
- Tag each mod as server-side, client-side, or both.
- Save the full setup as a named preset and switch between presets instantly.
- Keep separate presets for different mods, maps, or server variants.

Switch the active preset from the CLI:

```powershell
dzl preset load hardcore
```

[Go deeper →](/dzl/guides/server-instances/)
