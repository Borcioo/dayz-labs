---
title: Git & GitHub
description: Treat each mod project as a git repo, with GitHub publish and releases.
sidebar:
  order: 8
---

Your mod source lives in a normal, git-friendly project folder, and dzl treats it as exactly
that: a git repository you can manage without leaving the launcher. Check what's changed,
commit, branch, and push — then publish the whole thing to GitHub and cut releases when it's
ready to share.

Because the source sits outside the P: drive in a real project folder, version control just
works. You get the everyday git actions inline, plus the two big "ship it" moments — creating a
GitHub repo and tagging a release — handled through the GitHub CLI so you don't switch tools.

## What you can do

- See status, changed files, recent commits, and the working-tree diff for a mod.
- Stage and commit a mod's changes from the launcher.
- List, create, and check out branches; push and pull against a remote.
- Publish a new GitHub repo for a mod, and cut a release at the current commit — optionally
  attaching the built PBO as an asset.

These actions are available from the tray's per-mod Git window, the CLI, and over MCP.
