# Phase 3 — Dzl.Mcp Implementation Plan

> **For agentic workers:** Use subagent-driven-development. Steps use `- [ ]`.

**Goal:** An MCP stdio server exposing dzl as typed tools, so Claude Code drives the launcher through MCP instead of the "skill calls the CLI through cmd" workaround. Plus a shared `LauncherService` in Core that both the CLI and MCP call (DRY — removes the status/mods/preset logic currently inlined in the CLI).

**Architecture:** Extract a `LauncherService` into `Dzl.Core` that wraps a config path and returns plain serializable records for every operation (status/mods/presets/start/stop/restart/logs/set-preset), re-resolving the active profile on each call. The CLI is refactored to call it; a new `Dzl.Mcp` console project hosts the MCP server whose `[McpServerTool]` methods are thin wrappers over the same service.

**Tech Stack:** .NET 8, ModelContextProtocol (official C# SDK, prerelease), Microsoft.Extensions.Hosting. xUnit + FluentAssertions 6.12.1.

---

## File Structure

```
src/Dzl.Core/App/
  LauncherService.cs    # the shared facade + result records
src/Dzl.Mcp/
  Dzl.Mcp.csproj
  Program.cs            # generic host + MCP stdio server
  DzlMcpTools.cs        # [McpServerToolType] static methods -> LauncherService
tests/Dzl.Core.Tests/
  LauncherServiceTests.cs
```

---

## Task 3.1: LauncherService facade in Core

**Files:** Create `src/Dzl.Core/App/LauncherService.cs`; Test `tests/Dzl.Core.Tests/LauncherServiceTests.cs`

The service owns a `configPath`. Every read re-runs `Profiles.EnsureDefault` + `Profiles.ResolveActive` so it always reflects the active profile (the CLI does this per-invocation; the long-lived MCP server must do it per-call). Returns plain records (no UI/JSON concerns — callers serialize).

- [ ] **Step 1: Write the failing test** `LauncherServiceTests.cs`:
```csharp
using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;
using Xunit;

public class LauncherServiceTests
{
    private static string TmpConfig() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");

    [Fact]
    public void Status_reports_active_profile_and_down_targets()
    {
        var svc = new LauncherService(TmpConfig());
        var s = svc.Status();
        s.ActivePreset.Should().Be("default");      // EnsureDefault seeded it
        s.Mode.Should().Be("debug");
        s.Port.Should().Be(2302);
        s.Server.State.Should().Be("down");
        s.Client.State.Should().Be("down");
    }

    [Fact]
    public void Mods_lists_enabled_with_side()
    {
        var path = TmpConfig();
        var cfg = ConfigStore.Load(path) with { Mods = new() {
            new() { Path = @"P:\@CF", Enabled = true, Side = "both" },
            new() { Path = @"P:\@Off", Enabled = false, Side = "both" },
        }};
        Profiles.EnsureDefault(path);
        ConfigStore.Save(cfg, Profiles.PresetFile("default", path)); // active profile is 'default'
        var svc = new LauncherService(path);
        var mods = svc.Mods();
        mods.Should().ContainSingle(m => m.Path == @"P:\@CF" && m.Side == "both");
    }

    [Fact]
    public void Presets_marks_active_and_set_preset_switches()
    {
        var path = TmpConfig();
        var svc = new LauncherService(path);
        svc.SaveActivePresetAs("alpha");
        svc.Presets().Should().Contain(p => p.Name == "alpha" && p.Active);
        svc.SetPreset("default");
        svc.Presets().Should().Contain(p => p.Name == "default" && p.Active);
        svc.SetPreset("ghost").Ok.Should().BeFalse();  // unknown preset
    }

    [Fact]
    public void Logs_returns_empty_when_no_file()
    {
        var svc = new LauncherService(TmpConfig());
        svc.Logs("script", 10).Lines.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify fail** — `dotnet test --filter LauncherServiceTests` → FAIL (LauncherService missing).

- [ ] **Step 3: Implement** `src/Dzl.Core/App/LauncherService.cs`:
```csharp
using Dzl.Core.Config;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Mods;

namespace Dzl.Core.App;

public sealed record TargetState(string State, string? Source, string? Mode, int? Pid);
public sealed record StatusReport(
    string Mode, int Port, string? ActivePreset,
    TargetState Server, TargetState Client,
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyList<ModView> Mods,
    IReadOnlyDictionary<string, string?> Logs);
public sealed record ModView(string Path, string Side);
public sealed record PresetView(string Name, bool Active);
public sealed record LogsResult(string Which, string? Path, IReadOnlyList<string> Lines);
public sealed record OpResult(bool Ok, string Message);

public sealed class LauncherService
{
    private readonly string _configPath;
    public LauncherService(string configPath) { _configPath = configPath; }

    private (DzlConfig cfg, string savePath, string active) Resolve()
    {
        Profiles.EnsureDefault(_configPath);
        return Profiles.ResolveActive(_configPath);
    }

    private TargetState TargetOf(IReadOnlyDictionary<string, ProcInfo> live, string t)
        => live.TryGetValue(t, out var i)
            ? new TargetState("up", i.Source, i.Mode, i.Pid)
            : new TargetState("down", null, null, null);

    public StatusReport Status()
    {
        var (cfg, _, active) = Resolve();
        var live = StateFile.ReadLive(_configPath, ProcessManager.ImageOf);
        var logs = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath);
        var paths = new Dictionary<string, string>
        {
            ["dayz_path"] = cfg.DayzPath,
            ["profiles_path"] = cfg.ProfilesPath,
            ["client_profiles_path"] = cfg.ClientProfilesPath,
            ["config_dir"] = Path.GetDirectoryName(_configPath) ?? ".",
            ["presets_dir"] = Profiles.PresetsDir(_configPath),
        };
        var mods = cfg.Mods.Where(m => m.Enabled).Select(m => new ModView(m.Path, m.Side)).ToList();
        return new StatusReport(cfg.Mode, cfg.Port, string.IsNullOrEmpty(active) ? null : active,
            TargetOf(live, "server"), TargetOf(live, "client"), paths, mods, logs);
    }

    public IReadOnlyList<ModView> Mods()
    {
        var (cfg, _, _) = Resolve();
        return cfg.Mods.Where(m => m.Enabled).Select(m => new ModView(m.Path, m.Side)).ToList();
    }

    public IReadOnlyList<PresetView> Presets()
    {
        var (_, _, active) = Resolve();
        return Profiles.List(_configPath).Select(n => new PresetView(n, n == active)).ToList();
    }

    public OpResult SetPreset(string name)
    {
        if (!Profiles.List(_configPath).Contains(name)) return new OpResult(false, $"no preset '{name}'");
        Profiles.SetActive(name, _configPath);
        return new OpResult(true, $"active preset -> '{name}'");
    }

    public OpResult SaveActivePresetAs(string name)
    {
        var (cfg, _, _) = Resolve();
        Profiles.Save(cfg, name, _configPath);
        Profiles.SetActive(name, _configPath);
        return new OpResult(true, $"saved & active: '{name}'");
    }

    public LogsResult Logs(string which, int lines)
    {
        var (cfg, _, _) = Resolve();
        var path = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath).GetValueOrDefault(which);
        var tail = path is null ? new List<string>() : LogTail.LastLines(path, lines);
        return new LogsResult(which, path, tail);
    }

    public OpResult Start(string mode, bool client)
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Spawn(mode, "server", cfg, "mcp", _configPath);
        if (client) ProcessManager.Spawn(mode, "client", cfg, "mcp", _configPath);
        return new OpResult(true, $"started server{(client ? " + client" : "")} ({mode})");
    }

    public OpResult Stop(bool client)
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Stop("server", cfg, _configPath);
        if (client) ProcessManager.Stop("client", cfg, _configPath);
        return new OpResult(true, $"stopped server{(client ? " + client" : "")}");
    }

    public OpResult Restart(string mode)
    {
        var (cfg, _, _) = Resolve();
        ProcessManager.Restart(mode, cfg, _configPath, "mcp");
        return new OpResult(true, $"restarted server ({mode})");
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test` → all pass (35 total: 31 prior + 4 new).

- [ ] **Step 5: Commit**
```bash
git add -A && git commit -m "feat(core): LauncherService facade shared by CLI + MCP (DRY)"
```

## Task 3.2: Refactor Dzl.Cli onto LauncherService

**Files:** Modify `src/Dzl.Cli/Program.cs`

Replace the inlined status-object construction, mods listing, and preset list/save/load logic with calls to `LauncherService` (serialize its records with `ConfigStore.Json` for `--json`). Keep `config set/add-root/rm-root` as-is (those edit + save the config directly; not part of the service). Keep `start/stop/restart` either direct (`ProcessManager`) or via the service — prefer the service for consistency, but the CLI's `source` marker is `"cli"`, so for start/stop/restart KEEP the direct `ProcessManager` calls with `source:"cli"` (the service hardcodes `"mcp"`). Only move the read/preset operations to the service.

- [ ] **Step 1:** Refactor `status` handler to `new LauncherService(configPath).Status()` then serialize/print (JSON and plain forms). Refactor `mods` to `.Mods()`. Refactor `preset` list to `.Presets()`, `preset save` to `.SaveActivePresetAs(name)`, `preset load` to `.SetPreset(name)` (print its `OpResult.Message`; exit 1 when `!Ok`).
- [ ] **Step 2: Verify** the same smoke as Phase 2 still produces equivalent output:
```bash
dotnet run --project src/Dzl.Cli -- --config D:\Projekty\dzl-dotnet\.clismoke\config.json status --json
dotnet run --project src/Dzl.Cli -- --config D:\Projekty\dzl-dotnet\.clismoke\config.json preset
```
Expected: `"active_preset": "default"`, both targets `"state": "down"`, preset prints `* default`. Then `rm -rf D:/Projekty/dzl-dotnet/.clismoke`.
- [ ] **Step 3:** `dotnet build` (0 warnings) and `dotnet test` (35 pass).
- [ ] **Step 4: Commit** `refactor(cli): use shared LauncherService for status/mods/preset (DRY)`

## Task 3.3: Dzl.Mcp stdio server + tools

**Files:** Create `src/Dzl.Mcp/Dzl.Mcp.csproj`, `Program.cs`, `DzlMcpTools.cs`

The MCP server resolves its config path from env var `DZL_CONFIG` if set, else `%LOCALAPPDATA%\dzl\config.json`. Tools are thin wrappers over `LauncherService` returning JSON strings (predictable, same shape the CLI emits).

- [ ] **Step 1: Scaffold**
```bash
dotnet new console -n Dzl.Mcp -o src/Dzl.Mcp -f net8.0
dotnet sln add src/Dzl.Mcp
dotnet add src/Dzl.Mcp reference src/Dzl.Core
dotnet add src/Dzl.Mcp package ModelContextProtocol --prerelease
dotnet add src/Dzl.Mcp package Microsoft.Extensions.Hosting
rm src/Dzl.Mcp/Program.cs
```

- [ ] **Step 2: Implement** `src/Dzl.Mcp/Program.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
// stdout is reserved for the MCP protocol — all logging must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
```
and `src/Dzl.Mcp/DzlMcpTools.cs`:
```csharp
using System.ComponentModel;
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;
using ModelContextProtocol.Server;

namespace Dzl.Mcp;

[McpServerToolType]
public static class DzlMcpTools
{
    private static string ConfigPath() =>
        Environment.GetEnvironmentVariable("DZL_CONFIG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dzl", "config.json");

    private static LauncherService Svc() => new(ConfigPath());
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    [McpServerTool, Description("Get running state, mode, port, active profile, paths, enabled mods and newest log files.")]
    public static string Status() => J(Svc().Status());

    [McpServerTool, Description("List the enabled mods (path + side) of the active profile.")]
    public static string ListMods() => J(Svc().Mods());

    [McpServerTool, Description("List profiles/presets; the active one is flagged.")]
    public static string ListPresets() => J(Svc().Presets());

    [McpServerTool, Description("Switch the active profile by name.")]
    public static string SetPreset([Description("Preset name")] string name) => J(Svc().SetPreset(name));

    [McpServerTool, Description("Read the last N lines of a log: script|rpt|adm|client.")]
    public static string Logs([Description("script|rpt|adm|client")] string which,
                              [Description("How many trailing lines")] int lines = 50)
        => J(Svc().Logs(which, lines));

    [McpServerTool, Description("Start the server (and optionally the client). mode = debug|normal.")]
    public static string Start([Description("debug|normal")] string mode = "debug",
                               [Description("also start the client")] bool client = false)
        => J(Svc().Start(mode, client));

    [McpServerTool, Description("Stop the server (and optionally the client).")]
    public static string Stop([Description("also stop the client")] bool client = false) => J(Svc().Stop(client));

    [McpServerTool, Description("Restart the server. mode = debug|normal.")]
    public static string Restart([Description("debug|normal")] string mode = "debug") => J(Svc().Restart(mode));
}
```

- [ ] **Step 3: Verify build** — `dotnet build` → 0 warnings / 0 errors. If the SDK's fluent API names differ on the restored prerelease version (e.g. `WithToolsFromAssembly` vs `WithTools`), adapt to the actual API; the goal is a stdio server that auto-registers the `[McpServerTool]` methods. Document any deviation.

- [ ] **Step 4: Smoke the protocol** — drive one stdio round-trip. Create `D:\Projekty\dzl-dotnet\.mcpsmoke.jsonl` with an initialize + tools/list sequence and pipe it in:
```bash
DZL_CONFIG="D:\\Projekty\\dzl-dotnet\\.clismoke\\config.json"
printf '%s\n' \
'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}' \
'{"jsonrpc":"2.0","method":"notifications/initialized"}' \
'{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
| DZL_CONFIG="$DZL_CONFIG" dotnet run --project src/Dzl.Mcp 2>/dev/null | head -40
```
Expected: JSON-RPC responses where the `tools/list` result lists `Status`, `ListMods`, `ListPresets`, `SetPreset`, `Logs`, `Start`, `Stop`, `Restart`. (If running via the Bash tool hits a sandbox write-denial as in Phase 2, run the same through PowerShell.) Confirm the tool names appear, then `rm -rf D:/Projekty/dzl-dotnet/.clismoke` and remove any temp jsonl.

- [ ] **Step 5: Commit** `feat(mcp): stdio MCP server exposing dzl tools over LauncherService`

---

## Acceptance (whole phase)
- `dotnet test` green (35).
- `Dzl.Mcp` builds; `tools/list` over stdio returns the 8 tools.
- Registering it in a Claude Code session (`claude mcp add dzl -- dotnet <abs>/src/Dzl.Mcp/bin/Debug/net8.0/Dzl.Mcp.dll`, with `DZL_CONFIG` pointed at the real config) lets `Status`/`Start`/`Logs` be called — verified manually outside this plan.

## Notes for later
- The MCP `source` marker is `"mcp"` so the tray/CLI status can show who started the server (human via TUI/CLI vs assistant via MCP) — same live-state model as the Python MVP.
- When Phase 4 (tray + IPC) lands, both CLI and MCP should prefer the named-pipe client when the tray is up, falling back to direct `LauncherService` when it isn't. `LauncherService` is the natural place to add that branch.
