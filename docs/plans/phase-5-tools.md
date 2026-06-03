# Phase 5 — DayZ Tools integration (tier 1 launch + tier 2 wrap + P: drive)

> **For agentic workers:** subagent-driven-development. Steps use `- [ ]`. Process-spawning wrappers are manual-verify; pure parts (catalog mapping, glob, suffix validation, arg assembly) are unit-tested.

**Goal:** Turn dzl into a hub for the DayZ Tools suite. **Tier 1:** discover the tool exes under `<DayZ Tools>\Bin\` and launch them (Workbench, Object Builder, Terrain Builder, Publisher, …). **Tier 2:** wrap the CLI-capable tools with real UX — batch PNG→PAA (ImageToPAA), pack PBO (AddonBuilder), unbinarize config (CfgConvert). Plus detect/mount the `P:` work drive. Exposed through CLI, MCP, and the tray.

**Architecture:** A `ToolCatalog` in Core maps tool keys to relative `Bin\` exe paths (real paths verified against an install) with a glob fallback, tagging each `LaunchOnly` or `CliWrappable`. `ToolLauncher` starts a GUI tool. Typed wrappers (`ImageToPaa`, `AddonBuilder`, `CfgConvert`) assemble args + run the exe with progress/output capture. `WorkDrive` checks/mounts `P:`. All three front-ends (CLI/MCP/tray) call these Core services.

**Tech Stack:** .NET 8, System.Diagnostics.Process. Tests: xUnit + FluentAssertions 6.12.1.

**Verified real layout** (from a live `E:\Steam\steamapps\common\DayZ Tools\Bin`):
```
Bin\Workbench\workbenchApp.exe          Bin\ObjectBuilder\ObjectBuilder.exe
Bin\TerrainBuilder\terrainBuilder.exe   Bin\Publisher\Publisher.exe
Bin\Launcher\DayZToolsLauncher.exe      Bin\CeEditor\CeEditor.exe
Bin\NavMeshGenerator\NavMeshGenerator_x64.exe   Bin\TerrainProcessor\TerrainProcessor.exe
Bin\ImageToPAA\ImageToPAA.exe           Bin\AddonBuilder\AddonBuilder.exe
Bin\CfgConvert\CfgConvert.exe           Bin\Binarize\binarize.exe
Bin\PboUtils\BankRev.exe (extract pbo)  Bin\PboUtils\FileBank.exe
Bin\DsUtils\DSSignFile.exe              Bin\DsUtils\DSCreateKey.exe
Bin\WorkDrive\WorkDrive.exe (mounts P:)
```

---

## File Structure
```
src/Dzl.Core/Tools/
  ToolCatalog.cs    # ToolEntry + Discover(toolsPath)  (TESTED: mapping/glob/Kind)
  ToolLauncher.cs   # Launch(entry) -> Process          (manual)
  ImageToPaa.cs     # Convert/ConvertFolder + PaaSuffix  (TESTED: suffix, glob, args)
  AddonBuilder.cs   # PackArgs(...) + Pack(...)          (TESTED: PackArgs)
  CfgConvert.cs     # Unbinarize(...) + UnbinarizeArgs   (TESTED: args)
  WorkDrive.cs      # IsMounted/Mount/Unmount            (TESTED: IsMounted)
tests/Dzl.Core.Tests/
  ToolCatalogTests.cs  ToolWrappersTests.cs
```

---

## Task 5.1: ToolCatalog — discover + classify tool exes

**Files:** Create `src/Dzl.Core/Tools/ToolCatalog.cs`, `src/Dzl.Core/Tools/ToolLauncher.cs`; Test `tests/Dzl.Core.Tests/ToolCatalogTests.cs`.

- [ ] **Step 1: Write the failing test** `ToolCatalogTests.cs`:
```csharp
using Dzl.Core.Tools;
using FluentAssertions;
using Xunit;

public class ToolCatalogTests
{
    private static string FakeInstall()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        void Exe(string rel)
        {
            var p = Path.Combine(root, "Bin", rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "");
        }
        Exe("Workbench/workbenchApp.exe");
        Exe("ImageToPAA/ImageToPAA.exe");
        Exe("AddonBuilder/AddonBuilder.exe");
        // a tool NOT in the known map, to exercise the glob fallback:
        Exe("SomeFutureTool/futuretool.exe");
        return root;
    }

    [Fact]
    public void Discover_finds_known_tools_with_correct_kind()
    {
        var cat = ToolCatalog.Discover(FakeInstall());
        cat.Should().Contain(t => t.Key == "workbench" && t.Exists && t.Kind == ToolKind.LaunchOnly);
        cat.Should().Contain(t => t.Key == "imagetopaa" && t.Exists && t.Kind == ToolKind.CliWrappable);
        cat.Should().Contain(t => t.Key == "addonbuilder" && t.Exists && t.Kind == ToolKind.CliWrappable);
    }

    [Fact]
    public void Known_tool_missing_on_disk_is_reported_not_exists()
    {
        // empty install: known tools present in the map but Exists=false
        var empty = Directory.CreateTempSubdirectory().FullName;
        var cat = ToolCatalog.Discover(empty);
        cat.Should().Contain(t => t.Key == "workbench" && !t.Exists);
    }

    [Fact]
    public void Glob_fallback_surfaces_unmapped_exes()
    {
        var cat = ToolCatalog.Discover(FakeInstall());
        cat.Should().Contain(t => t.ExePath.EndsWith("futuretool.exe") && t.Kind == ToolKind.LaunchOnly);
    }

    [Fact]
    public void Missing_tools_path_returns_known_map_all_absent()
    {
        var cat = ToolCatalog.Discover(@"X:\does\not\exist");
        cat.Should().NotBeEmpty();
        cat.Should().OnlyContain(t => !t.Exists);
    }
}
```

- [ ] **Step 2: Run to verify fail** — `dotnet test --filter ToolCatalogTests` → FAIL.

- [ ] **Step 3: Implement** `src/Dzl.Core/Tools/ToolCatalog.cs`:
```csharp
namespace Dzl.Core.Tools;

public enum ToolKind { LaunchOnly, CliWrappable }

public sealed record ToolEntry(string Key, string DisplayName, string ExePath, bool Exists, ToolKind Kind);

public static class ToolCatalog
{
    // key -> (display, relative path under Bin\, kind). Verified against a real install.
    private static readonly (string Key, string Name, string Rel, ToolKind Kind)[] Known =
    {
        ("workbench",       "Workbench",         @"Workbench\workbenchApp.exe",            ToolKind.LaunchOnly),
        ("objectbuilder",   "Object Builder",    @"ObjectBuilder\ObjectBuilder.exe",       ToolKind.LaunchOnly),
        ("terrainbuilder",  "Terrain Builder",   @"TerrainBuilder\terrainBuilder.exe",     ToolKind.LaunchOnly),
        ("publisher",       "Publisher",         @"Publisher\Publisher.exe",               ToolKind.LaunchOnly),
        ("launcher",        "DayZ Tools Launcher",@"Launcher\DayZToolsLauncher.exe",       ToolKind.LaunchOnly),
        ("ceeditor",        "CE Editor",         @"CeEditor\CeEditor.exe",                 ToolKind.LaunchOnly),
        ("terrainprocessor","Terrain Processor", @"TerrainProcessor\TerrainProcessor.exe", ToolKind.LaunchOnly),
        ("navmesh",         "NavMesh Generator", @"NavMeshGenerator\NavMeshGenerator_x64.exe", ToolKind.LaunchOnly),
        ("imagetopaa",      "ImageToPAA",        @"ImageToPAA\ImageToPAA.exe",             ToolKind.CliWrappable),
        ("addonbuilder",    "Addon Builder",     @"AddonBuilder\AddonBuilder.exe",         ToolKind.CliWrappable),
        ("cfgconvert",      "CfgConvert (DeRap)",@"CfgConvert\CfgConvert.exe",             ToolKind.CliWrappable),
        ("binarize",        "Binarize",          @"Binarize\binarize.exe",                 ToolKind.CliWrappable),
        ("bankrev",         "BankRev (extract PBO)",@"PboUtils\BankRev.exe",               ToolKind.CliWrappable),
        ("filebank",        "FileBank (pack PBO)",  @"PboUtils\FileBank.exe",              ToolKind.CliWrappable),
        ("dssignfile",      "DSSignFile (sign)", @"DsUtils\DSSignFile.exe",                ToolKind.CliWrappable),
    };

    public static List<ToolEntry> Discover(string toolsPath)
    {
        var bin = Path.Combine(toolsPath, "Bin");
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ToolEntry>();
        foreach (var (key, name, rel, kind) in Known)
        {
            var full = Path.Combine(bin, rel);
            known.Add(Path.GetFullPath(full));
            list.Add(new ToolEntry(key, name, full, File.Exists(full), kind));
        }
        // glob fallback: any other *.exe directly under Bin\<dir>\ not already mapped.
        if (Directory.Exists(bin))
        {
            foreach (var exe in Directory.EnumerateFiles(bin, "*.exe", SearchOption.AllDirectories))
            {
                if (known.Contains(Path.GetFullPath(exe))) continue;
                var key = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                list.Add(new ToolEntry(key, Path.GetFileNameWithoutExtension(exe), exe, true, ToolKind.LaunchOnly));
            }
        }
        return list;
    }

    public static ToolEntry? Find(string toolsPath, string key) =>
        Discover(toolsPath).FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
```

`src/Dzl.Core/Tools/ToolLauncher.cs` (manual):
```csharp
using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class ToolLauncher
{
    // Launch a tool GUI (fire-and-forget). Returns false if the exe is missing.
    public static bool Launch(ToolEntry tool)
    {
        if (!tool.Exists) return false;
        Process.Start(new ProcessStartInfo(tool.ExePath) { UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(tool.ExePath)! });
        return true;
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test` → all pass (45 total: 41 + 4). Build 0 warnings.
- [ ] **Step 5: Commit** `feat(core): ToolCatalog discover/classify DayZ Tools exes + ToolLauncher`

## Task 5.2: ImageToPaa wrapper (batch PNG→PAA + suffix validation)

**Files:** Create `src/Dzl.Core/Tools/ImageToPaa.cs`; Test in `tests/Dzl.Core.Tests/ToolWrappersTests.cs`.

ImageToPAA CLI: `ImageToPAA.exe <input> <output.paa>`. DayZ textures must carry a suffix: `_co` (color), `_nohq` (normal), `_smdi` (specular), `_as`, etc. `ConvertFolder` globs `*.png`/`*.tga`, maps each to `<name>.paa`, and flags names missing a known suffix.

- [ ] **Step 1: Write the failing tests** (create `ToolWrappersTests.cs`):
```csharp
using Dzl.Core.Tools;
using FluentAssertions;
using Xunit;

public class ToolWrappersTests
{
    [Theory]
    [InlineData("rock_co.png", true)]
    [InlineData("rock_nohq.tga", true)]
    [InlineData("metal_smdi.png", true)]
    [InlineData("diffuse.png", false)]   // no DayZ suffix
    public void Paa_suffix_validation(string file, bool ok)
        => ImageToPaa.HasValidSuffix(file).Should().Be(ok);

    [Fact]
    public void Convert_args_are_input_then_output_paa()
    {
        var args = ImageToPaa.ConvertArgs(@"P:\t\rock_co.png");
        args.input.Should().Be(@"P:\t\rock_co.png");
        args.output.Should().Be(@"P:\t\rock_co.paa");
    }

    [Fact]
    public void Plan_folder_lists_png_and_tga_with_suffix_flags()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "rock_co.png"), "");
        File.WriteAllText(Path.Combine(dir, "bad.png"), "");
        File.WriteAllText(Path.Combine(dir, "note.txt"), "");
        var plan = ImageToPaa.PlanFolder(dir, recursive: false);
        plan.Should().HaveCount(2);                                   // only images
        plan.Should().Contain(i => i.Input.EndsWith("rock_co.png") && i.SuffixOk);
        plan.Should().Contain(i => i.Input.EndsWith("bad.png") && !i.SuffixOk);
    }
}
```

- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement** `src/Dzl.Core/Tools/ImageToPaa.cs`:
```csharp
using System.Diagnostics;

namespace Dzl.Core.Tools;

public sealed record PaaJob(string Input, string Output, bool SuffixOk);
public sealed record PaaResult(string Input, string Output, bool Ok, string Message);

public static class ImageToPaa
{
    private static readonly string[] Suffixes =
        { "_co", "_ca", "_nohq", "_smdi", "_as", "_dt", "_mc", "_nofhq", "_sky", "_detail" };

    public static bool HasValidSuffix(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return Suffixes.Any(s => stem.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    public static (string input, string output) ConvertArgs(string input) =>
        (input, Path.ChangeExtension(input, ".paa"));

    public static List<PaaJob> PlanFolder(string dir, bool recursive)
    {
        if (!Directory.Exists(dir)) return new();
        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(dir, "*.*", opt)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            .Select(f => { var (i, o) = ConvertArgs(f); return new PaaJob(i, o, HasValidSuffix(f)); })
            .ToList();
    }

    // Manual-verify: runs the real exe per job, reports per-file result + progress.
    public static List<PaaResult> ConvertFolder(string exePath, string dir, bool recursive,
                                                IProgress<PaaResult>? progress = null)
    {
        var results = new List<PaaResult>();
        foreach (var job in PlanFolder(dir, recursive))
        {
            var psi = new ProcessStartInfo(exePath) { RedirectStandardError = true, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(job.Input);
            psi.ArgumentList.Add(job.Output);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            var r = new PaaResult(job.Input, job.Output, p.ExitCode == 0 && File.Exists(job.Output),
                p.ExitCode == 0 ? "ok" : $"exit {p.ExitCode}: {err.Trim()}");
            results.Add(r); progress?.Report(r);
        }
        return results;
    }
}
```
- [ ] **Step 4:** run → PASS (48 total). Build 0 warnings.
- [ ] **Step 5: Commit** `feat(core): ImageToPaa wrapper — batch PNG/TGA->PAA + suffix validation`

## Task 5.3: AddonBuilder (pack PBO) + CfgConvert (unbinarize) + WorkDrive (P:)

**Files:** Create `src/Dzl.Core/Tools/AddonBuilder.cs`, `CfgConvert.cs`, `WorkDrive.cs`; add tests to `ToolWrappersTests.cs`.

AddonBuilder CLI: `AddonBuilder.exe <sourceDir> <outputDir> [-clear] [-packonly] [-prefix=<p>] [-sign=<keyfile>]`. CfgConvert: `CfgConvert.exe -txt -dst <out.cpp> <in.bin>` (unbinarize) / `-bin` (binarize). WorkDrive: `P:` is mounted iff the drive exists; mount via `WorkDrive.exe` (or `subst P: <path>` fallback).

- [ ] **Step 1: Write the failing tests** (append to `ToolWrappersTests.cs`):
```csharp
    [Fact]
    public void AddonBuilder_pack_args_assembled()
    {
        var args = AddonBuilder.PackArgs(@"P:\Mod\src", @"P:\out", clear: true, packOnly: true, prefix: "MyMod", signKey: null);
        args.Should().ContainInOrder(@"P:\Mod\src", @"P:\out", "-clear", "-packonly", "-prefix=MyMod");
        args.Should().NotContain(a => a.StartsWith("-sign"));
    }

    [Fact]
    public void AddonBuilder_pack_args_include_sign_when_key_given()
        => AddonBuilder.PackArgs(@"s", @"o", false, false, null, @"P:\keys\my.biprivatekey")
            .Should().Contain("-sign=P:\\keys\\my.biprivatekey");

    [Fact]
    public void CfgConvert_unbinarize_args()
        => CfgConvert.UnbinarizeArgs(@"P:\m\config.bin", @"P:\m\config.cpp")
            .Should().ContainInOrder("-txt", "-dst", @"P:\m\config.cpp", @"P:\m\config.bin");

    [Fact]
    public void WorkDrive_is_mounted_checks_directory()
    {
        // a path that exists stands in for a mounted drive
        var dir = Directory.CreateTempSubdirectory().FullName;
        WorkDrive.IsMounted(dir).Should().BeTrue();
        WorkDrive.IsMounted(@"X:\definitely\not\there").Should().BeFalse();
    }
```

- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement** the three files.

`src/Dzl.Core/Tools/AddonBuilder.cs`:
```csharp
using System.Diagnostics;

namespace Dzl.Core.Tools;

public sealed record PackResult(bool Ok, int ExitCode, string Output);

public static class AddonBuilder
{
    public static List<string> PackArgs(string sourceDir, string outputDir, bool clear, bool packOnly,
                                        string? prefix, string? signKey)
    {
        var a = new List<string> { sourceDir, outputDir };
        if (clear) a.Add("-clear");
        if (packOnly) a.Add("-packonly");
        if (!string.IsNullOrWhiteSpace(prefix)) a.Add($"-prefix={prefix}");
        if (!string.IsNullOrWhiteSpace(signKey)) a.Add($"-sign={signKey}");
        return a;
    }

    public static PackResult Pack(string exePath, string sourceDir, string outputDir,
        bool clear = true, bool packOnly = true, string? prefix = null, string? signKey = null)
    {
        var psi = new ProcessStartInfo(exePath) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in PackArgs(sourceDir, outputDir, clear, packOnly, prefix, signKey)) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new PackResult(p.ExitCode == 0, p.ExitCode, outp.Trim());
    }
}
```

`src/Dzl.Core/Tools/CfgConvert.cs`:
```csharp
using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class CfgConvert
{
    public static List<string> UnbinarizeArgs(string binPath, string outCpp) =>
        new() { "-txt", "-dst", outCpp, binPath };

    public static (bool ok, string output) Unbinarize(string exePath, string binPath, string outCpp)
    {
        var psi = new ProcessStartInfo(exePath) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in UnbinarizeArgs(binPath, outCpp)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode == 0, outp.Trim());
    }
}
```

`src/Dzl.Core/Tools/WorkDrive.cs`:
```csharp
using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class WorkDrive
{
    // P: is the conventional DayZ work drive. Mounted iff the path exists.
    public static bool IsMounted(string drive = @"P:\") => Directory.Exists(drive);

    // Mount via DayZ Tools' WorkDrive.exe if present, else `subst P: <path>`.
    public static bool Mount(string workDriveExeOrSourcePath, string drive = "P:")
    {
        if (IsMounted(drive + "\\")) return true;
        ProcessStartInfo psi;
        if (File.Exists(workDriveExeOrSourcePath) &&
            workDriveExeOrSourcePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            psi = new ProcessStartInfo(workDriveExeOrSourcePath) { UseShellExecute = true };
        else
            psi = new ProcessStartInfo("subst", $"{drive} \"{workDriveExeOrSourcePath}\"") { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return IsMounted(drive + "\\");
    }

    public static void Unmount(string drive = "P:")
    {
        using var p = Process.Start(new ProcessStartInfo("subst", $"{drive} /D") { UseShellExecute = false, CreateNoWindow = true });
        p?.WaitForExit();
    }
}
```

- [ ] **Step 4:** run → PASS (52 total). Build 0 warnings.
- [ ] **Step 5: Commit** `feat(core): AddonBuilder pack + CfgConvert unbinarize + WorkDrive P: mount`

## Task 5.4: Surfaces — CLI verbs, MCP tools, tray Tools menu

**Files:** Modify `src/Dzl.Cli/Program.cs`, `src/Dzl.Mcp/DzlMcpTools.cs`, `src/Dzl.Tray/TrayIcon.cs`. (No new Core tests; smoke + build.)

The tools path comes from the active config (`DzlConfig.DayzToolsPath`). Resolve config the same way each front-end already does.

**CLI** — add a `tools` command group + wrappers:
- `dzl tools` / `dzl tools list` — print each `ToolEntry` as `key   DisplayName   [present|missing]   (launch|cli)`.
- `dzl tools open <key>` — `ToolLauncher.Launch(ToolCatalog.Find(toolsPath,key))`; error if missing.
- `dzl paa <dir> [--recursive]` — `ImageToPaa.ConvertFolder(<imagetopaa exe>, dir, recursive, progress→stdout)`; print per-file ok/fail + a summary; warn on suffix misses (use `PlanFolder` to pre-report names missing a suffix).
- `dzl pack <src> <dst> [--prefix p] [--sign key] [--no-clear]` — `AddonBuilder.Pack(...)`; print exit + output.
- `dzl derap <config.bin> [<out.cpp>]` — `CfgConvert.Unbinarize(...)` (default out = same name `.cpp`).
- `dzl workdrive status|mount|unmount` — `WorkDrive.IsMounted()` / `Mount(<WorkDrive.exe or source>)` / `Unmount()`.

**MCP** (`DzlMcpTools.cs`) — add tools returning JSON:
- `list_tools()` → catalog. `open_tool(key)` → launch result.
- `convert_paa(dir, recursive=false)` → results. `pack_pbo(src, dst, prefix?, sign?)` → PackResult. `unbinarize(bin, outCpp?)`.
- `workdrive(action)` where action ∈ status|mount|unmount.
Resolve toolsPath via `new LauncherService(ConfigPath())` → actually read the config: use `Profiles.ResolveActive(ConfigPath()).cfg.DayzToolsPath`.

**Tray** (`TrayIcon.cs`) — add a **"Tools ▸"** submenu built from `ToolCatalog.Discover(toolsPath)`: one click-to-launch item per present `LaunchOnly` tool; a separator; "Pack PBO…" and "Batch PAA…" entries (these can, for now, just open the relevant tool or a simple folder-pick via `OpenFileDialog`/`FolderBrowserDialog` — keep minimal, build-verified). Plus a "Work drive: P: ✓/✗" status item that mounts when clicked if unmounted.

- [ ] **Step 1:** Implement CLI `tools`/`paa`/`pack`/`derap`/`workdrive`. Smoke (PowerShell): `dotnet run --project src/Dzl.Cli -- --config <tmp> tools list` lists the catalog (most `missing` against a tmp config whose DayzToolsPath is the real install → many `present`). Point a tmp config's `dayz_tools_path` at the real `E:\Steam\steamapps\common\DayZ Tools` to see `present` rows, OR just confirm the command runs and prints rows. Clean up tmp.
- [ ] **Step 2:** Implement MCP tools; build; optional stdio smoke confirms `tools/list` now includes `list_tools`, `open_tool`, `convert_paa`, `pack_pbo`, `unbinarize`, `workdrive`.
- [ ] **Step 3:** Implement tray Tools submenu (build-verified; human checks visually).
- [ ] **Step 4:** `dotnet build` 0 errors, `dotnet test` 52 pass. Commit `feat: DayZ Tools surfaces — CLI verbs, MCP tools, tray submenu`.

---

## Acceptance (whole phase)
- `dotnet test` green (52).
- `dzl tools list` enumerates the suite; against the real install, Workbench/ObjectBuilder/ImageToPAA/AddonBuilder show `present`.
- MCP `tools/list` includes the 6 new tool methods.
- Tray has a Tools ▸ submenu + P: status item (human-verified).
- `dzl workdrive status` reports P: state; `dzl pack`/`dzl paa`/`dzl derap` run the real exes when the work drive + tools are present (human-verified end-to-end).

## Notes
- Pure arg-assembly + catalog mapping + suffix validation are unit-tested; the exe-spawning methods (Launch/ConvertFolder/Pack/Unbinarize/Mount) are manual-verified, mirroring how ProcessManager was handled.
- `DayzToolsPath` already exists in `DzlConfig` — no schema change.
- Later: a Params/keys field for signing (`-sign`) could move into config; for now it's a CLI/MCP arg.
