# Contributing to DayZ Labs (`dzl`)

Thanks for your interest in improving `dzl`! This is a Windows-only DayZ
mod-development launcher built on .NET 8. Bug reports, fixes, features, and docs are
all welcome.

## Ways to contribute

- **Report a bug** or **request a feature** via the
  [issue tracker](https://github.com/Borcioo/dayz-labs/issues) — use the templates so
  the version and environment details come through up front.
- **Improve the docs** at
  [borcioo.github.io/dayz-labs](https://borcioo.github.io/dayz-labs/) (site source lives
  in `web/`).
- **Send a pull request** for a fix or feature.

## Getting set up

You need the **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**, plus
**DayZ** and **DayZ Tools** (from Steam) if you want to exercise the launcher end to end.

```powershell
dotnet build Dzl.sln -c Debug      # build everything
dotnet test                        # run the tests (xUnit + FluentAssertions)
.\run-dzl.bat                      # build + launch the tray, de-elevated via explorer.exe
```

`run-dzl.bat` launches the tray **de-elevated** on purpose: the tray mounts the **P:**
work drive, and a mounted drive is only visible in the session that mounted it. Prefer
this script over `dotnet run` for the tray.

> If the tray app is running it can lock the solution build. Run a focused test project
> instead, e.g. `dotnet test tests/Dzl.Core.Tests`.

## Architecture in one minute

Frontends are thin; all real work lives in **`Dzl.Core`**:

```
Dzl.Cli  ─┐
Dzl.Mcp  ─┼─> Dzl.Core (LauncherService = the one entry point)
Dzl.Tray ─┘
```

When you add a capability, add it to `LauncherService` first, then surface it in each
frontend (CLI, MCP, tray). See [CLAUDE.md](CLAUDE.md) for the full architecture notes.

## Coding conventions

- **Warnings are errors.** `TreatWarningsAsErrors` is on globally — a warning fails the
  build. Keep the tree clean.
- **Records + immutable `with` updates** for config and state; split pure helpers out
  from I/O so they can be unit-tested (`ArgvBuilder`, the `WorkDrive` arg-builders).
- **Process wrappers around external exes never throw** — they return `(ok, output)` or
  a bool.
- **MCP: stdout is the protocol.** All logging in `Dzl.Mcp` must go to stderr.
- Match the style of the code around you.

## Tests

Add or update tests for any behaviour change — the pure helpers are the easy,
high-value place to test. Make sure `dotnet test` is green before opening a PR.

## Pull requests

- Keep PRs focused: one logical change per PR.
- Use [Conventional Commits](https://www.conventionalcommits.org) for commit subjects
  (`feat:`, `fix:`, `docs:`, `ci:`, …) — the history follows this.
- Fill in the PR template and link any related issue.
- By contributing, you agree your work is licensed under the project's
  **[GPL-3.0](LICENSE)**.
