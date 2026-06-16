---
title: Build pipeline
description: A guarded, verifying build around a single Addon Builder call.
sidebar:
  order: 5
---

Packing a mod is one Addon Builder call — but Addon Builder is a black box that can mangle a
config, exit "successfully" on a no-op, and leave you shipping a broken PBO. dzl wraps that
single call in a pipeline that checks your work before, during, and after the build, so the
PBO that lands is one you can trust.

A **preflight gate** runs first and catches the classic ship-it bugs — the worst being a
baked `P:\...` absolute path that works on your machine and breaks on everyone else's. Then
dzl builds, verifies a fresh PBO actually appeared with the right prefix and signature, and
swaps it into place atomically so a failed rebuild never leaves a half-written mod for the
server to load.

## What you can do

- Run preflight to catch config, asset-reference, baked-path, and path-hygiene problems early.
- Build straight from your versioned source, with optional binarization and signing.
- Skip unchanged mods automatically with a content-hash cache (and `--force` when you mean it).
- Trust a "build succeeded" message that's verified against the real output, not the tool's
  exit code.

From the CLI:

```powershell
dzl preflight MyMod
dzl build MyMod --sign
```

[Go deeper →](/dayz-labs/guides/building-mods/)
