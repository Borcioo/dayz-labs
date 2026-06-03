# Phase 4 — Dzl.Tray (tray app + named-pipe IPC) Implementation Plan

> **For agentic workers:** subagent-driven-development. Steps use `- [ ]`. WPF UI cannot be visually verified in this environment — UI tasks are build-verified by agents and **functionally verified by the human** running the app.

**Goal:** A resident WPF tray app that owns live launcher state and hosts a named-pipe IPC server, so a server started from the CLI/MCP shows up in the tray within ~1s and Stop from the tray kills the right PID — one source of truth, robust (no state-file race). Plus a main window (mod checklist, controls, profile switcher, live logs).

**Architecture:** A pure `IpcDispatcher.Handle(request, service)` maps JSON-RPC-ish requests to `LauncherService` calls (testable without pipes). `PipeServer` (thin) hosts it on a named pipe; `PipeClient` connects. A `ControlPlane` facade routes every operation through the pipe when the tray is up, else calls `LauncherService` directly — CLI and MCP both go through it so their actions reflect in the tray. The WPF app is single-instance (Mutex), shows a tray icon (status colour + context menu) and a main window; it runs the `PipeServer` and polls live state.

**Tech Stack:** .NET 8 WPF (`net8.0-windows`, `<UseWPF>true</UseWPF>`), H.NotifyIcon.Wpf (tray), CommunityToolkit.Mvvm (bindings), System.IO.Pipes (IPC), System.Text.Json. Tests: xUnit + FluentAssertions 6.12.1.

---

## File Structure
```
src/Dzl.Core/Ipc/
  IpcContract.cs     # IpcRequest / IpcResponse records + pipe name constant
  IpcDispatcher.cs   # pure Handle(request, service) -> response  (TESTED)
  PipeServer.cs      # named-pipe host loop (thin, manual)
  PipeClient.cs      # connect + send (thin, manual)
  ControlPlane.cs    # pipe-or-direct facade  (fallback TESTED)
src/Dzl.Tray/
  Dzl.Tray.csproj
  App.xaml(.cs)      # single-instance, start hidden, host PipeServer
  TrayIcon.cs        # H.NotifyIcon + status poll + context menu
  MainWindow.xaml(.cs)
  ViewModels/MainViewModel.cs
tests/Dzl.Core.Tests/
  IpcDispatcherTests.cs
  ControlPlaneTests.cs
```

Prereq tweak (small Core change): give `LauncherService.Start/Stop/Restart` an optional `string source = "cli"` parameter (currently hardcoded "mcp"). MCP passes "mcp", CLI "cli", tray "tui". Do this in Task 4.1 Step 0.

---

## Task 4.1: IPC contract + pure dispatcher + thin transport

**Files:** Create `src/Dzl.Core/Ipc/{IpcContract.cs,IpcDispatcher.cs,PipeServer.cs,PipeClient.cs}`; Test `tests/Dzl.Core.Tests/IpcDispatcherTests.cs`. Modify `src/Dzl.Core/App/LauncherService.cs` (add `source` param).

- [ ] **Step 0: add `source` param to LauncherService**

In `LauncherService.cs` change the three signatures (keep bodies, thread `source` into `ProcessManager`):
```csharp
public OpResult Start(string mode, bool client, string source = "cli")
{
    var (cfg, _, _) = Resolve();
    ProcessManager.Spawn(mode, "server", cfg, source, _configPath);
    if (client) ProcessManager.Spawn(mode, "client", cfg, source, _configPath);
    return new OpResult(true, $"started server{(client ? " + client" : "")} ({mode})");
}
public OpResult Stop(bool client, string source = "cli")  // source unused by Stop; keep for symmetry
{
    var (cfg, _, _) = Resolve();
    ProcessManager.Stop("server", cfg, _configPath);
    if (client) ProcessManager.Stop("client", cfg, _configPath);
    return new OpResult(true, $"stopped server{(client ? " + client" : "")}");
}
public OpResult Restart(string mode, string source = "cli")
{
    var (cfg, _, _) = Resolve();
    ProcessManager.Restart(mode, cfg, _configPath, source);
    return new OpResult(true, $"restarted server ({mode})");
}
```
Update `Dzl.Mcp/DzlMcpTools.cs` Start/Stop/Restart calls to pass `"mcp"` (e.g. `Svc().Start(mode, client, "mcp")`). Build + the 35 tests must still pass.

- [ ] **Step 1: Write the failing test** `IpcDispatcherTests.cs`:
```csharp
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Ipc;
using FluentAssertions;
using Xunit;

public class IpcDispatcherTests
{
    private static LauncherService Svc() =>
        new(Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json"));

    [Fact]
    public void Status_request_returns_ok_with_json()
    {
        var r = IpcDispatcher.Handle(new IpcRequest("status", null), Svc());
        r.Ok.Should().BeTrue();
        r.Json.Should().Contain("active_preset");
    }

    [Fact]
    public void Set_preset_unknown_returns_ok_false_message()
    {
        var r = IpcDispatcher.Handle(new IpcRequest("set_preset", new() { ["name"] = "ghost" }), Svc());
        // dispatcher succeeds at routing; the op result inside reports failure
        r.Ok.Should().BeTrue();
        r.Json.Should().Contain("no preset");
    }

    [Fact]
    public void Unknown_method_returns_error()
    {
        var r = IpcDispatcher.Handle(new IpcRequest("frobnicate", null), Svc());
        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("unknown method");
    }

    [Fact]
    public void Request_response_round_trip_json()
    {
        var req = new IpcRequest("logs", new() { ["which"] = "script", ["lines"] = "5" });
        var json = System.Text.Json.JsonSerializer.Serialize(req, IpcContract.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<IpcRequest>(json, IpcContract.Json)!;
        back.Method.Should().Be("logs");
        back.Args!["which"].Should().Be("script");
    }
}
```

- [ ] **Step 2: Run to verify fail** — `dotnet test --filter IpcDispatcherTests` → FAIL.

- [ ] **Step 3: Implement**

`src/Dzl.Core/Ipc/IpcContract.cs`:
```csharp
using System.Text.Json;

namespace Dzl.Core.Ipc;

public sealed record IpcRequest(string Method, Dictionary<string, string>? Args);
public sealed record IpcResponse(bool Ok, string? Error, string? Json);

public static class IpcContract
{
    public const string PipeName = "dzl-ipc-v1";
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}
```

`src/Dzl.Core/Ipc/IpcDispatcher.cs`:
```csharp
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Core.Ipc;

public static class IpcDispatcher
{
    private static string Arg(IpcRequest r, string k, string dflt = "") =>
        r.Args is not null && r.Args.TryGetValue(k, out var v) ? v : dflt;
    private static bool Flag(IpcRequest r, string k) =>
        bool.TryParse(Arg(r, k, "false"), out var b) && b;
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    public static IpcResponse Handle(IpcRequest req, LauncherService svc)
    {
        try
        {
            return req.Method switch
            {
                "status"       => new(true, null, J(svc.Status())),
                "mods"         => new(true, null, J(svc.Mods())),
                "presets"      => new(true, null, J(svc.Presets())),
                "set_preset"   => new(true, null, J(svc.SetPreset(Arg(req, "name")))),
                "save_preset"  => new(true, null, J(svc.SaveActivePresetAs(Arg(req, "name")))),
                "logs"         => new(true, null, J(svc.Logs(Arg(req, "which", "script"),
                                      int.TryParse(Arg(req, "lines", "50"), out var n) ? n : 50))),
                "start"        => new(true, null, J(svc.Start(Arg(req, "mode", "debug"), Flag(req, "client"), Arg(req, "source", "cli")))),
                "stop"         => new(true, null, J(svc.Stop(Flag(req, "client")))),
                "restart"      => new(true, null, J(svc.Restart(Arg(req, "mode", "debug"), Arg(req, "source", "cli")))),
                _              => new(false, $"unknown method: {req.Method}", null),
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, ex.Message, null);
        }
    }
}
```

`src/Dzl.Core/Ipc/PipeServer.cs` (thin, manual-verify):
```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Dzl.Core.App;

namespace Dzl.Core.Ipc;

// Hosts the dispatcher on a named pipe. One request line -> one response line.
// Run on a background task from the tray; cancel via the token.
public sealed class PipeServer
{
    private readonly Func<LauncherService> _svc;
    public PipeServer(Func<LauncherService> svc) { _svc = svc; }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(IpcContract.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(ct); }
            catch (OperationCanceledException) { break; }
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(ct);
                if (line is null) continue;
                var req = JsonSerializer.Deserialize<IpcRequest>(line, IpcContract.Json);
                var resp = req is null
                    ? new IpcResponse(false, "bad request", null)
                    : IpcDispatcher.Handle(req, _svc());
                await writer.WriteLineAsync(JsonSerializer.Serialize(resp, IpcContract.Json));
            }
            catch (IOException) { /* client vanished; loop */ }
        }
    }
}
```

`src/Dzl.Core/Ipc/PipeClient.cs` (thin, manual-verify):
```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Dzl.Core.Ipc;

public static class PipeClient
{
    // Returns null if no server is listening within the timeout.
    public static IpcResponse? Send(IpcRequest req, int timeoutMs = 300)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(timeoutMs);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(req, IpcContract.Json));
            var line = reader.ReadLine();
            return line is null ? null : JsonSerializer.Deserialize<IpcResponse>(line, IpcContract.Json);
        }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }
    }

    public static bool IsServerUp() => Send(new IpcRequest("status", null)) is not null;
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test` → all pass (39 total: 35 + 4). Also `dotnet build` 0 warnings.

- [ ] **Step 5: Commit** `feat(core): IPC contract + pure dispatcher + named-pipe transport`

## Task 4.2: ControlPlane (pipe-or-direct) + route CLI through it

**Files:** Create `src/Dzl.Core/Ipc/ControlPlane.cs`; Test `tests/Dzl.Core.Tests/ControlPlaneTests.cs`; Modify `src/Dzl.Cli/Program.cs`.

ControlPlane is what callers use. For each op: if `PipeClient` reaches a server, send the request and return the parsed `Json`; else call `LauncherService` directly and serialize. Status/mods/presets return JSON strings; start/stop/restart/set_preset return the op message.

- [ ] **Step 1: Write the failing test** `ControlPlaneTests.cs`:
```csharp
using Dzl.Core.Ipc;
using FluentAssertions;
using Xunit;

public class ControlPlaneTests
{
    // No tray/pipe server running in the test -> ControlPlane must fall back to direct LauncherService.
    [Fact]
    public void Falls_back_to_direct_when_no_server()
    {
        var cfg = Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");
        var cp = new ControlPlane(cfg);
        var statusJson = cp.StatusJson();
        statusJson.Should().Contain("active_preset").And.Contain("default");
    }

    [Fact]
    public void Set_preset_unknown_falls_back_and_reports()
    {
        var cfg = Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");
        var cp = new ControlPlane(cfg);
        cp.SetPresetJson("ghost").Should().Contain("no preset");
    }
}
```

- [ ] **Step 2:** run → FAIL.

- [ ] **Step 3: Implement** `src/Dzl.Core/Ipc/ControlPlane.cs`:
```csharp
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Core.Ipc;

// Routes through the tray's pipe when it's up, else operates directly.
public sealed class ControlPlane
{
    private readonly string _configPath;
    public ControlPlane(string configPath) { _configPath = configPath; }
    private LauncherService Direct() => new(_configPath);
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    private string Route(IpcRequest req, Func<LauncherService, object> direct)
    {
        var resp = PipeClient.Send(req);
        if (resp is not null && resp.Ok && resp.Json is not null) return resp.Json;
        return J(direct(Direct()));
    }

    public string StatusJson() => Route(new("status", null), s => s.Status());
    public string ModsJson() => Route(new("mods", null), s => s.Mods());
    public string PresetsJson() => Route(new("presets", null), s => s.Presets());
    public string SetPresetJson(string name) => Route(new("set_preset", new() { ["name"] = name }), s => s.SetPreset(name));
    public string SavePresetJson(string name) => Route(new("save_preset", new() { ["name"] = name }), s => s.SaveActivePresetAs(name));
    public string LogsJson(string which, int lines) =>
        Route(new("logs", new() { ["which"] = which, ["lines"] = lines.ToString() }), s => s.Logs(which, lines));
    public string StartJson(string mode, bool client, string source) =>
        Route(new("start", new() { ["mode"] = mode, ["client"] = client.ToString(), ["source"] = source }), s => s.Start(mode, client, source));
    public string StopJson(bool client) =>
        Route(new("stop", new() { ["client"] = client.ToString() }), s => s.Stop(client));
    public string RestartJson(string mode, string source) =>
        Route(new("restart", new() { ["mode"] = mode, ["source"] = source }), s => s.Restart(mode, source));
}
```

- [ ] **Step 4:** run → PASS (41 total). Build 0 warnings.

- [ ] **Step 5: wire the CLI** — In `Program.cs`, route the live operations through `ControlPlane` so a running tray reflects CLI actions: `status` → `new ControlPlane(configPath).StatusJson()` (for --json; for plain, deserialize or keep LauncherService for the human format — simplest: keep `LauncherService.Status()` for plain text, use ControlPlane only when the tray might be up… to keep it simple, route `start`/`stop`/`restart` through ControlPlane with source "cli", and leave `status`/`mods`/`preset` reads on the direct LauncherService as they are). Concretely: change only the `start`, `stop`, `restart` handlers to call `new ControlPlane(configPath).StartJson(mode, client, "cli")` / `.StopJson(client)` / `.RestartJson(mode, "cli")` and echo a short confirmation. Verify smoke: with no tray, `start --debug --dry-run` is unaffected (dry-run stays direct — keep dry-run on Launcher/ArgvBuilder, do NOT route dry-run through ControlPlane).

- [ ] **Step 6: Commit** `feat: ControlPlane pipe-or-direct routing; CLI start/stop/restart via it`

---

## Task 4.3: WPF Dzl.Tray — single-instance, tray icon, context menu, host PipeServer (BUILD + MANUAL)

**Files:** Create `src/Dzl.Tray/{Dzl.Tray.csproj, App.xaml, App.xaml.cs, TrayIcon.cs}` and a minimal `MainWindow`.

> Agents: get it COMPILING and runnable; the human verifies the tray visually. No unit tests for WPF.

Scaffold:
```
dotnet new wpf -n Dzl.Tray -o src/Dzl.Tray -f net8.0-windows
dotnet sln add src/Dzl.Tray
dotnet add src/Dzl.Tray reference src/Dzl.Core
dotnet add src/Dzl.Tray package H.NotifyIcon.Wpf
dotnet add src/Dzl.Tray package CommunityToolkit.Mvvm
```
`Dzl.Tray.csproj` must set `<OutputType>WinExe</OutputType>`, `<UseWPF>true</UseWPF>`, `<TargetFramework>net8.0-windows</TargetFramework>`. (Note `TreatWarningsAsErrors` is inherited — WPF generated code is clean, but if H.NotifyIcon raises XAML analyzer warnings that are unavoidable, scope a `<NoWarn>` in this csproj and document it.)

Behaviour:
- **Single instance:** in `App.OnStartup`, acquire a named `Mutex("dzl-tray-singleton")`. If already held, exit (a future enhancement signals the running instance to show its window).
- **Config path:** `DZL_CONFIG` env var else `%LOCALAPPDATA%\dzl\config.json`. Call `Profiles.EnsureDefault`.
- **Host PipeServer:** start `new PipeServer(() => new LauncherService(configPath)).RunAsync(cts.Token)` on a background task at startup; cancel on exit. Now CLI/MCP actions route into this process.
- **Tray icon (H.NotifyIcon `TaskbarIcon`):** a `DispatcherTimer` (1.5s) calls `StateFile.ReadLive(configPath, ProcessManager.ImageOf)`; set the icon/tooltip to reflect server up (green) / down (grey). Context menu items: **Start server (debug)**, **Stop server**, **Restart server**, separator, **Open main window**, **Open config folder**, separator, **Quit**. Wire each to `ControlPlane`/`LauncherService` (Start/Stop/Restart use source "tui"); Quit cancels the pipe token and shuts down.
- **Start hidden to tray** (no main window shown on launch; `ShutdownMode=OnExplicitShutdown`).

Acceptance (human): run `dotnet run --project src/Dzl.Tray`; a tray icon appears; right-click → Start server → the server launches and, in a separate terminal, `dzl status` shows `server: up (tui...)`; tray turns green; Stop from the menu kills it. Build must be 0-error.

- [ ] Scaffold, set csproj flags.
- [ ] Implement App single-instance + PipeServer host + EnsureDefault.
- [ ] Implement TrayIcon with status poll + context menu wired to ControlPlane.
- [ ] `dotnet build` 0 errors; commit `feat(tray): WPF single-instance tray app + context menu + IPC host`.

## Task 4.4: WPF main window — mods, controls, profile switcher, live logs (BUILD + MANUAL)

**Files:** `src/Dzl.Tray/MainWindow.xaml(.cs)`, `ViewModels/MainViewModel.cs`.

> The visual port of the Textual TUI. Build-verified by agents, visually verified by the human. Use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).

Layout (Grid):
- **Left:** mod list — `ItemsControl`/`ListBox` of a `ModRowVm` (CheckBox bound to `Enabled`, order number, `Name`, side badge). A search `TextBox` filters. Buttons: move up/down, to top/bottom, cycle side. Backed by `ModDiscovery.Merge(cfg.Mods, ModDiscovery.Discover(cfg.ScanRoots))`. Save writes the order/enabled/side back to the active profile via `ConfigStore.Save(cfg, savePath)`.
- **Right top:** status bar (mode, port, active profile, server/client state) bound to a 1.5s poll.
- **Right body:** log panes — a `TabControl` or stacked read-only `TextBox`es for script/rpt/adm/client, each fed by `LogTail.Follow(path, line => append, ct)` on a background task; auto-scroll to end.
- **Bottom:** argv preview (`ArgvBuilder.Build(mode,target,cfg)` joined) + buttons Start/Stop/Restart server, Start/Stop client, mode toggle (debug/normal), profile dropdown (`Profiles.List`, switch via `ControlPlane`/`Profiles.SetActive`), Params editor (a simple dialog editing the per-mode params list).

Acceptance (human): open the window from the tray; toggle mods and reorder → the argv preview updates and the selection persists to the active profile; Start → logs stream live in the panes; switch profile → the list/params reload. Build 0-error.

- [ ] MainViewModel with observable mod rows, status, commands.
- [ ] MainWindow.xaml layout + bindings.
- [ ] Live log panes via LogTail.Follow.
- [ ] `dotnet build` 0 errors; commit `feat(tray): main window — mods, controls, profile switcher, live logs`.

---

## Acceptance (whole phase)
- `dotnet test` green (41).
- Solution builds incl. `Dzl.Tray` (net8.0-windows).
- Human run: tray app resident; CLI/MCP `start` reflects in the tray within ~1.5s; tray Stop kills the right PID; main window mods/logs/profile switch work.

## Notes
- The pipe makes the tray the authority while it's up; when it's down, CLI/MCP fall back to the state file (same model as the Python MVP, now race-free while the tray runs).
- Phase 5 (Tools) adds a `Tools ▸` submenu to the tray and tools commands to the window using the same Core services.
