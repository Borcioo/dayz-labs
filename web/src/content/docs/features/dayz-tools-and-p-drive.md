---
title: "DayZ Tools & the P: drive"
description: "Drive the DayZ Tools binaries and the P: work drive without the GUIs."
sidebar:
  order: 4
---

DayZ modding leans on a pile of separate tool windows — Addon Builder, ImageToPAA, CfgConvert,
the WorkDrive — and a special **P:** drive they all expect to be mounted. dzl wraps those tools
so you can run the everyday jobs from one place, and mount or check the P: drive without ever
opening the DayZ Tools GUI.

The P: drive only behaves if it's mounted **in your normal user session** — mount it as an
administrator and it lands in an invisible session the game can't read. dzl handles that for
you, so the drive is actually there when the tools and the game look for it.

## What you can do

- Discover and launch the DayZ Tools GUIs when you do need them.
- Batch-convert PNG/TGA textures to PAA in a folder.
- Pack a folder into a PBO, or unbinarize a config back to readable text.
- Mount, check, or unmount the P: work drive without opening the Tools.

Manage the work drive from the CLI:

```powershell
dzl workdrive status
dzl workdrive mount
```
