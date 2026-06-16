---
title: Logs & diagnosis
description: Tail the DayZ logs and turn known failures into cause and fix.
sidebar:
  order: 3
---

When a mod misbehaves, the answer is almost always in a log file — if you can find the right
one and read past the noise. dzl tails all four DayZ logs (**script**, **rpt**, **adm**, and
the **client** log) for you, and then goes a step further: it scans the output for known
failure signatures and explains them in plain "cause → fix" terms.

Instead of staring at a wall of red text, you get a short list of what actually went wrong —
a mod whose signature was rejected, a version mismatch between client and server, a build-tool
symptom — each paired with what to do about it.

## What you can do

- Read the last N lines of any DayZ log on demand.
- Watch logs live while you reproduce a problem.
- Run the diagnoser to match known failures (signature kicks like missing or patched signs,
  mod version skew, build-tool symptoms) into cause-and-fix entries.
- Skip the guesswork on the most common "why did it kick me" failures.

From the CLI:

```powershell
dzl logs script --lines 100      # tail the last 100 lines
dzl logs script --diagnose       # run the diagnoser over the tail
```
