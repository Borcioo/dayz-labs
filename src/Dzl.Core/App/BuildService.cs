using Dzl.Core.Build;
using Dzl.Core.Build.Preflight;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Tools;

namespace Dzl.Core.App;

/// <summary>
/// Build→deploy facade: packs a mod project into a loadable PBO and registers it in the
/// active server's run-list. Pure bits live in <see cref="ModBuild"/>.
/// </summary>
/// <remarks>
/// Source lives at <c>&lt;ProjectsRoot&gt;\mods\&lt;Mod&gt;</c> and is read via its <c>P:\&lt;Mod&gt;</c>
/// junction; output lands at <c>&lt;ProjectsRoot&gt;\build\@&lt;Mod&gt;\Addons\</c> and is surfaced
/// (and registered) as <c>P:\Mods\@&lt;Mod&gt;</c> via the build-area junction.
/// </remarks>
public sealed class BuildService
{
    private readonly string _configPath;
    public BuildService(string configPath) { _configPath = configPath; }

    private string ConfigDir => Path.GetDirectoryName(_configPath) ?? ".";

    // Explicit config value, else the cached author handle, else "". One key signs all of a creator's mods.
    private string KeyName(DzlConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.SigningKey) ? cfg.SigningKey.Trim()
        : (ModScaffold.CachedAuthor(ConfigDir) ?? "").Trim();

    /// <summary>One signing key found in the keys folder. <see cref="HasPublic"/> false = the
    /// .bikey half is missing (signing works, but servers have nothing to whitelist).</summary>
    public sealed record SigningKeyInfo(string Name, string PrivateKeyPath, bool HasPublic);

    /// <summary>Enumerate signing keys (<c>*.biprivatekey</c>) in the resolved keys folder.</summary>
    public IReadOnlyList<SigningKeyInfo> ListKeys()
    {
        Profiles.EnsureDefault(_configPath);
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var keysDir = ProjectPaths.KeysDir(ProjectPaths.Root(cfg), cfg.KeysDir);
        if (!Directory.Exists(keysDir)) return Array.Empty<SigningKeyInfo>();
        return Directory.EnumerateFiles(keysDir, "*.biprivatekey")
            .Select(p => new SigningKeyInfo(
                Path.GetFileNameWithoutExtension(p), p,
                File.Exists(Path.ChangeExtension(p, ".bikey"))))
            .OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Resolved, read-only preview of where a build will read from / write to and which tool it
    /// will use — so a UI can pre-fill the paths and warn before running. No side effects.</summary>
    public sealed record BuildPlanView(
        bool Ok, string Mod, string ProjectDir, string SourceOnP, string OutputDir, string AddonsDir,
        string AddonBuilderExe, bool WorkDriveMounted, bool Ready, string Message,
        string KeyName, string PrivateKeyPath, bool HasKey);

    public BuildPlanView Plan(string mod)
    {
        if (!ProjectPaths.IsValidName(mod))
            return new BuildPlanView(false, mod, "", "", "", "", "", false, false, $"invalid mod name: {mod}", "", "", false);

        Profiles.EnsureDefault(_configPath);
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var root = ProjectPaths.Root(cfg);
        var projectDir = ProjectPaths.ModDir(root, mod);
        var src = ProjectPaths.WorkDriveLink(mod);
        var exe = ToolCatalog.Find(cfg.DayzToolsPath, "binarize");

        var isProject = Directory.Exists(projectDir) && ModProjects.IsProject(projectDir);
        var haveTool = exe is not null && exe.Exists;
        var pMounted = WorkDrive.IsMounted();
        var ready = isProject && haveTool && pMounted;
        var msg = !isProject ? "not a mod project ($PBOPREFIX$ / config.cpp missing)"
                : !haveTool ? "binarize.exe not found — set the DayZ Tools path"
                : !pMounted ? "P: work drive not mounted — mount it to build"
                : "ready to build";

        var keyName = KeyName(cfg);
        var keyPath = keyName.Length > 0 ? ProjectPaths.PrivateKey(root, cfg.KeysDir, keyName) : "";
        var hasKey = keyPath.Length > 0 && File.Exists(keyPath);

        return new BuildPlanView(true, mod, projectDir, src,
            ProjectPaths.BuildDir(root, mod), ProjectPaths.BuildAddonsDir(root, mod),
            exe?.ExePath ?? "(not found)", pMounted, ready, msg,
            keyName, keyPath, hasKey);
    }

    /// <summary>Create the creator's signing key pair (DSCreateKey) in the keys folder. One key for all mods;
    /// the name is the config value or the cached author. Idempotent-ish: refuses if the key already exists.</summary>
    public KeyResult GenerateKey(string? keyNameOverride = null)
    {
        Profiles.EnsureDefault(_configPath);
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var name = !string.IsNullOrWhiteSpace(keyNameOverride) ? keyNameOverride!.Trim() : KeyName(cfg);
        if (name.Length == 0)
            return new KeyResult(false, "", "", "no signing-key name — set one in Settings or pass a name");
        if (!ProjectPaths.IsValidName(name))
            return new KeyResult(false, "", "", $"invalid key name: {name} (letters/digits/underscore, start with a letter)");

        var root = ProjectPaths.Root(cfg);
        var keysDir = ProjectPaths.KeysDir(root, cfg.KeysDir);
        // Never regenerate over an existing private key — a lost .biprivatekey means every mod
        // signed with it can no longer be updated. The existing pair is returned untouched.
        if (File.Exists(ProjectPaths.PrivateKey(root, cfg.KeysDir, name)))
            return new KeyResult(true, ProjectPaths.PrivateKey(root, cfg.KeysDir, name),
                ProjectPaths.PublicKey(root, cfg.KeysDir, name),
                $"key '{name}' already exists — left untouched (existing keys are never overwritten)");

        var exe = ToolCatalog.Find(cfg.DayzToolsPath, "dscreatekey");
        if (exe is null || !exe.Exists)
            return new KeyResult(false, "", "", "DSCreateKey not found — check the DayZ Tools path");

        return DsTools.CreateKey(exe.ExePath, keysDir, name);
    }

    /// <summary>Run the preflight rule set over a mod project. Read-only on the project; writes
    /// only the report files.</summary>
    /// <remarks>The CfgConvert syntax gate engages automatically when DayZ Tools is configured;
    /// the work drive is used for vanilla-reference resolution only when mounted.</remarks>
    public PreflightView Preflight(string modName, bool saveReport = true)
    {
        if (!ProjectPaths.IsValidName(modName))
        {
            var bad = new PreflightReport();
            bad.Error("mod-name", $"invalid mod name: {modName}");
            return new PreflightView(false, modName, 1, 0, 0, bad.Findings, "", "");
        }

        Profiles.EnsureDefault(_configPath);
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var root = ProjectPaths.Root(cfg);
        var modDir = ProjectPaths.ModDir(root, modName);
        var cfgConvert = ToolCatalog.Find(cfg.DayzToolsPath, "cfgconvert");

        var opts = new PreflightOptions
        {
            WorkDriveRoot = WorkDrive.IsMounted() ? @"P:\" : null,
            CfgConvertExe = cfgConvert?.Exists == true ? cfgConvert.ExePath : null,
            TempDir = Path.Combine(ConfigDir, "temp"),
        };

        var report = PreflightEngine.Run(modDir, modName, opts);

        string txt = "", json = "";
        if (saveReport && Directory.Exists(modDir))
            (txt, json) = ReportExport.Save(report, modName,
                Path.Combine(ProjectPaths.BuildDir(root, modName), "preflight-report"));

        return new PreflightView(report.Ok, modName, report.Errors, report.Warnings, report.Infos,
            report.Findings, txt, json);
    }

    /// <summary>Build <paramref name="modName"/> and (on success) add it to the active run-list.</summary>
    /// <param name="clean">Pass <c>-clear</c> to AddonBuilder (wipe the temp/output first).</param>
    /// <param name="binarize">Binarize configs/models (AddonBuilder default). <c>false</c> = <c>-packonly</c>.</param>
    /// <param name="onLine">Optional live-log sink for each AddonBuilder output line.</param>
    /// <param name="sign">Sign the PBO with the creator's key (AddonBuilder <c>-sign</c>); copies the public
    /// <c>.bikey</c> into the mod's <c>keys\</c> so it ships. Fails if no key exists (generate one first).</param>
    /// <param name="force">Ignore the skip-unchanged cache and rebuild regardless.</param>
    /// <param name="keyName">Sign with this key from the keys folder instead of the configured/default one.</param>
    public BuildResult Build(string modName, bool clean = false, bool binarize = true, bool sign = false, Action<string>? onLine = null, bool force = false, string? keyName = null, bool ignorePreflightErrors = false)
    {
        if (!ProjectPaths.IsValidName(modName))
            return FailResult(modName, null, $"invalid mod name: {modName}");

        Profiles.EnsureDefault(_configPath);
        var (cfg, _, active) = Profiles.ResolveActive(_configPath);

        var root = ProjectPaths.Root(cfg);
        var projectDir = ProjectPaths.ModDir(root, modName);
        var (exe, envFail) = ValidateEnvironment(modName, cfg, projectDir);
        if (exe is null)
            return envFail!;

        // The preflight view rides along on every result from here on.
        var (preflight, gateFail) = GateOnPreflight(modName, cfg, onLine, ignorePreflightErrors);
        if (gateFail is not null)
            return gateFail;

        var (signKey, effectiveKey, keyFail) = ResolveSignKey(modName, cfg, root, sign, keyName, preflight, onLine);
        if (keyFail is not null)
            return keyFail;

        var workDriveSource = EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath);
        var buildDir = ProjectPaths.BuildDir(root, modName);
        var addonsDir = ProjectPaths.BuildAddonsDir(root, modName);
        Directory.CreateDirectory(addonsDir);

        var (stateHash, cache, skip) = TryCacheSkip(modName, projectDir, exe.ExePath, buildDir, addonsDir,
            binarize, sign, force, signKey, preflight, onLine);
        if (skip is not null)
            return skip;

        var junctionFail = EnsureJunctions(modName, workDriveSource, projectDir, root, preflight);
        if (junctionFail is not null)
            return junctionFail;

        // Direct engine: Binarize (excluding ODOL p3d, copied as-is) → CfgConvert → PboWriter (in-process) → sign.
        // Output lands in a work Addons, then publishes atomically into the loadable @<Mod>\Addons.
        var workDir = Path.Combine(buildDir, ".work");
        var workAddons = Path.Combine(workDir, "Addons");
        try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { }
        Directory.CreateDirectory(workAddons);
        var prefix = PathResolver.ReadPrefix(projectDir);
        var startUtc = DateTime.UtcNow;
        var eng = BuildEngine.Run(cfg.DayzToolsPath, ProjectPaths.WorkDriveLink(modName),
            prefix: prefix.Length > 0 ? prefix : modName, pboName: modName,
            workDir: Path.Combine(workDir, "engine"), outAddonsDir: workAddons,
            binarize: binarize, signPrivateKey: signKey, onLine: onLine);
        if (!eng.Ok || !File.Exists(eng.Pbo))
            return FailResult(modName, preflight, $"build failed: {eng.Output}", eng.Output);

        var (pbo, publishFail) = PublishAndRecord(modName, root, projectDir, workDir, workAddons, addonsDir,
            eng.Pbo, startUtc, stateHash, cache, eng.Output, preflight);
        if (publishFail is not null)
            return publishFail;

        if (sign)
            ShipPublicKey(cfg, root, modName, effectiveKey);

        var (registered, note) = RegisterInRunList(cfg, active, modName);
        return new BuildResult(true, modName, buildDir, pbo, registered, note, eng.Output, "", preflight);
    }

    public sealed record PackPreflight(string Child, string Dir, PreflightView View);

    /// <summary>Preflight each (selected) inner mod of a pack — same checks the pack build gates on, so a UI
    /// can show findings before building. Read-only.</summary>
    public IReadOnlyList<PackPreflight> PreflightPack(string packName, IReadOnlyList<string>? selected = null)
    {
        var results = new List<PackPreflight>();
        if (!ProjectPaths.IsValidName(packName)) return results;
        Profiles.EnsureDefault(_configPath);
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var root = ProjectPaths.Root(cfg);
        var pack = ModProjects.Discover(root).FirstOrDefault(p =>
            p.IsPack && string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));
        if (pack is null) return results;

        IEnumerable<ModProject> children = pack.Children;
        if (selected is { Count: > 0 })
        {
            var set = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            children = children.Where(c => set.Contains(c.Name));
        }

        var cfgConvert = ToolCatalog.Find(cfg.DayzToolsPath, "cfgconvert");
        var opts = new PreflightOptions
        {
            WorkDriveRoot = WorkDrive.IsMounted() ? @"P:\" : null,
            CfgConvertExe = cfgConvert?.Exists == true ? cfgConvert.ExePath : null,
            TempDir = Path.Combine(ConfigDir, "temp"),
        };
        foreach (var c in children)
        {
            var r = PreflightEngine.Run(c.Path, c.Name, opts);
            results.Add(new PackPreflight(c.Name, c.Path,
                new PreflightView(r.Ok, c.Name, r.Errors, r.Warnings, r.Infos, r.Findings, "", "")));
        }
        return results;
    }

    public sealed record PackChildResult(string Name, bool Ok, string Message);
    public sealed record PackBuildResult(
        bool Ok, string Pack, string OutputDir, string AddonsDir, bool Registered,
        IReadOnlyList<PackChildResult> Children, string Message, string Output);

    /// <summary>Build every (selected) inner mod of a PACK into one shared <c>@&lt;pack&gt;\Addons</c> (many
    /// PBOs) + <c>keys\</c>, then register the pack as a single loadable mod. AddonBuilder reads each child
    /// via <c>P:\&lt;pack&gt;\&lt;child&gt;</c> (so the PBO is named after the child); all child PBOs are staged
    /// and published in one atomic swap, so a rebuild replaces the whole pack output. v1: full rebuild of the
    /// selected children (no skip-unchanged cache yet).</summary>
    public PackBuildResult BuildPack(string packName, IReadOnlyList<string>? selected = null,
        bool binarize = true, bool sign = false, Action<string>? onLine = null, string? keyName = null,
        bool ignorePreflightErrors = false)
    {
        PackBuildResult Fail(string msg, string output = "", IReadOnlyList<PackChildResult>? kids = null) =>
            new(false, packName, "", "", false, kids ?? System.Array.Empty<PackChildResult>(), msg, output);

        if (!ProjectPaths.IsValidName(packName))
            return Fail($"invalid pack name: {packName}");

        Profiles.EnsureDefault(_configPath);
        var (cfg, _, active) = Profiles.ResolveActive(_configPath);
        var root = ProjectPaths.Root(cfg);

        var pack = ModProjects.Discover(root).FirstOrDefault(p =>
            p.IsPack && string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));
        if (pack is null)
            return Fail($"'{packName}' is not a pack (a folder whose subfolders are mods) under mods\\");

        IEnumerable<ModProject> children = pack.Children;
        if (selected is { Count: > 0 })
        {
            var set = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            children = children.Where(c => set.Contains(c.Name));
        }
        var build = children.ToList();
        if (build.Count == 0)
            return Fail("no inner mods to build (none selected / none found)");

        var bin = ToolCatalog.Find(cfg.DayzToolsPath, "binarize");
        if (bin is null || !bin.Exists)
            return Fail("binarize.exe not found — set the DayZ Tools path");
        if (!WorkDrive.IsMounted())
            return Fail("P: work drive not mounted — mount it first");

        var (signKey, effectiveKey, keyFail) = ResolveSignKey(packName, cfg, root, sign, keyName, null, onLine);
        if (keyFail is not null)
            return Fail(keyFail.Message);

        // Preflight each child up front; one child's errors block the whole pack (mirrors single-mod gating).
        if (cfg.PreflightBeforeBuild)
        {
            var cfgConvert = ToolCatalog.Find(cfg.DayzToolsPath, "cfgconvert");
            var opts = new PreflightOptions
            {
                WorkDriveRoot = @"P:\",
                CfgConvertExe = cfgConvert?.Exists == true ? cfgConvert.ExePath : null,
                TempDir = Path.Combine(ConfigDir, "temp"),
            };
            foreach (var c in build)
            {
                onLine?.Invoke($"preflight: {c.Name} ...");
                var pf = PreflightEngine.Run(c.Path, c.Name, opts);
                if (pf.Ok) continue;

                foreach (var f in pf.Findings.Where(f => f.Severity == FindingSeverity.Error))
                    onLine?.Invoke($"preflight ✗ {c.Name}/{f.Rule}: {f.Message}");
                // "Build anyway": report the errors but don't block (e.g. references to vanilla assets not
                // extracted on P: that the engine resolves at runtime).
                if (ignorePreflightErrors)
                    onLine?.Invoke($"preflight: {c.Name} has {pf.Errors} error(s) — building anyway (errors ignored)");
                else
                    return Fail(
                        $"preflight failed for '{c.Name}' ({pf.Errors} error(s)) — fix them, tick \"build anyway\", or set preflight_before_build=false",
                        string.Join("\n", pf.Findings.Where(f => f.Severity == FindingSeverity.Error)
                            .Select(f => $"{c.Name}/{f.Rule}: {f.Message}")));
            }
        }

        var workDriveSource = EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath);
        var packDir = ProjectPaths.ModDir(root, packName);
        var srcEns = Junction.Ensure(ProjectPaths.JunctionPath(workDriveSource, packName), packDir);
        if (!srcEns.Ok) return Fail($"source junction P:\\{packName} → {packDir} failed: {srcEns.Detail}");
        var buildEns = Junction.Ensure(ProjectPaths.BuildAreaJunction(workDriveSource), ProjectPaths.BuildRoot(root));
        if (!buildEns.Ok) return Fail($"build junction failed: {buildEns.Detail}");

        var buildDir = ProjectPaths.BuildDir(root, packName);
        var addonsDir = ProjectPaths.BuildAddonsDir(root, packName);
        var workRoot = Path.Combine(buildDir, ".work");
        var staging = Path.Combine(workRoot, "Addons");
        if (Directory.Exists(workRoot)) try { Directory.Delete(workRoot, recursive: true); } catch { }
        Directory.CreateDirectory(staging);

        var results = new List<PackChildResult>();
        var output = new System.Text.StringBuilder();

        foreach (var c in build)
        {
            onLine?.Invoke($"=== building {c.Name} ===");
            var childWork = Path.Combine(workRoot, c.Name + "_work");
            var childOut = Path.Combine(workRoot, c.Name + "_out");
            // A pack child with no $PBOPREFIX$ must still get a UNIQUE prefix (<pack>\<child>) — the bare leaf
            // name collides with vanilla/other mods and breaks terrain loads (e.g. world.pbo prefix=world).
            var childPrefix = ProjectPaths.PackChildPrefix(packName, c.Name, PathResolver.ReadPrefix(c.Path));
            var eng = BuildEngine.Run(cfg.DayzToolsPath, c.Path,
                prefix: childPrefix, pboName: c.Name,
                workDir: childWork, outAddonsDir: childOut, binarize: binarize, signPrivateKey: signKey, onLine: onLine);
            output.AppendLine(eng.Output);
            if (!eng.Ok || !File.Exists(eng.Pbo))
            {
                results.Add(new PackChildResult(c.Name, false, eng.Output));
                return Fail($"'{c.Name}' build failed: {eng.Output}", output.ToString(), results);
            }
            // Collect this child's PBO (+ .bisign) into the shared staging Addons.
            foreach (var f in Directory.EnumerateFiles(childOut).Where(f =>
                         f.EndsWith(".pbo", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".bisign", StringComparison.OrdinalIgnoreCase)))
                File.Move(f, Path.Combine(staging, Path.GetFileName(f)), overwrite: true);
            results.Add(new PackChildResult(c.Name, true, "packed"));
            onLine?.Invoke($"    {c.Name}: ok");
        }

        var (pubOk, pubDetail) = ModBuild.PublishAtomically(staging, addonsDir);
        if (!pubOk)
            return Fail($"publish failed: {pubDetail}", output.ToString(), results);
        try { Directory.Delete(workRoot, recursive: true); } catch { /* harmless leftover */ }
        ModBuild.WriteMarker(ProjectPaths.BuildMarkerPath(root, packName),
            $"dzl-built pack {DateTime.UtcNow:O} ({results.Count} mod(s))");

        if (sign) ShipPublicKey(cfg, root, packName, effectiveKey);

        var (registered, _) = RegisterInRunList(cfg, active, packName);
        return new PackBuildResult(true, packName, buildDir, addonsDir, registered, results,
            $"built {results.Count(r => r.Ok)}/{results.Count} mod(s) into @{packName}", output.ToString());
    }

    private static BuildResult FailResult(string modName, PreflightView? preflight, string msg, string output = "") =>
        new(false, modName, "", "", false, msg, output,
            BuildDiagnostics.Format(BuildDiagnostics.Diagnose(output + "\n" + msg)), preflight);

    private static (ToolEntry? exe, BuildResult? fail) ValidateEnvironment(string modName, DzlConfig cfg, string projectDir)
    {
        if (!Directory.Exists(projectDir) || !ModProjects.IsProject(projectDir))
            return (null, FailResult(modName, null, $"not a mod project: {projectDir} (need $PBOPREFIX$ or config.cpp)"));

        var exe = ToolCatalog.Find(cfg.DayzToolsPath, "binarize");
        if (exe is null || !exe.Exists)
            return (null, FailResult(modName, null, "binarize.exe not found — set DayZ Tools path / install DayZ Tools"));

        if (!WorkDrive.IsMounted())
            return (null, FailResult(modName, null, "P: work drive not mounted — mount it first (binarize resolves vanilla data + includes against P:)"));

        return (exe, null);
    }

    // AddonBuilder reports "Build Successful" even for configs it silently mangles, so error-severity
    // findings block the build (preflight_before_build=false opts out). The view rides along on the
    // result so frontends can show the findings without a second run.
    private (PreflightView? preflight, BuildResult? fail) GateOnPreflight(string modName, DzlConfig cfg,
        Action<string>? onLine, bool ignoreErrors)
    {
        if (!cfg.PreflightBeforeBuild)
            return (null, null);

        onLine?.Invoke("preflight: checking project before build ...");
        var preflight = Preflight(modName, saveReport: true);
        foreach (var f in preflight.Findings.Where(f => f.Severity == FindingSeverity.Error))
            onLine?.Invoke($"preflight ✗ {f.Rule}: {f.Message}");
        if (!preflight.Ok)
        {
            // "Build anyway": preflight still runs and reports, but its errors don't block the build. Useful for
            // map mods that reference vanilla assets not extracted on P: (e.g. an .emat the engine resolves at
            // runtime) — the ref-missing errors are then false positives. The findings still ride along.
            if (ignoreErrors)
            {
                onLine?.Invoke($"preflight: {preflight.Errors} error(s) — building anyway (errors ignored)");
                return (preflight, null);
            }
            return (preflight, FailResult(modName, preflight,
                $"preflight failed with {preflight.Errors} error(s) — fix them, tick \"build anyway\", or set preflight_before_build=false",
                string.Join("\n", preflight.Findings
                    .Where(f => f.Severity == FindingSeverity.Error)
                    .Select(f => $"{f.Rule}: {f.Message}" + (f.File.Length > 0 ? $"  [{f.File}:{f.Line}]" : "")))));
        }
        onLine?.Invoke($"preflight: ok ({preflight.Warnings} warning(s))");
        return (preflight, null);
    }

    // Resolved up front so a missing key fails before the (slow) build, not after.
    private (string? signKey, string effectiveKey, BuildResult? fail) ResolveSignKey(
        string modName, DzlConfig cfg, string root, bool sign, string? keyName,
        PreflightView? preflight, Action<string>? onLine)
    {
        var effectiveKey = !string.IsNullOrWhiteSpace(keyName) ? keyName.Trim() : KeyName(cfg);
        if (!sign)
            return (null, effectiveKey, null);

        if (effectiveKey.Length == 0)
            return (null, effectiveKey, FailResult(modName, preflight, "sign requested but no signing-key name — set one in Settings"));
        var signKey = ProjectPaths.PrivateKey(root, cfg.KeysDir, effectiveKey);
        if (!File.Exists(signKey))
            return (null, effectiveKey, FailResult(modName, preflight, $"signing key '{effectiveKey}' not found at {signKey} — generate it first"));
        onLine?.Invoke($"signing with key: {effectiveKey}");
        return (signKey, effectiveKey, null);
    }

    // Skip-unchanged: same payload + same settings + output still present → nothing to do.
    private (string stateHash, Dictionary<string, BuildCache.Entry> cache, BuildResult? skip) TryCacheSkip(
        string modName, string projectDir, string exePath, string buildDir, string addonsDir,
        bool binarize, bool sign, bool force, string? signKey,
        PreflightView? preflight, Action<string>? onLine)
    {
        var sha1Memo = new Dictionary<string, string>();
        var settingsFingerprint =
            $"binarize={binarize};sign={sign};" +
            $"exe={BuildCache.Fingerprint(exePath, sha1Memo)};" +
            $"key={(sign ? BuildCache.Fingerprint(signKey, sha1Memo) : "off")}";
        var stateHash = BuildCache.ComputeStateHash(projectDir,
            PreflightOptions.DefaultExcludes, settingsFingerprint, sha1Memo);
        var cache = BuildCache.Load(ConfigDir);
        if (!force && cache.TryGetValue(modName, out var cached) && cached.Hash == stateHash &&
            File.Exists(cached.Pbo) &&
            (!sign || Directory.EnumerateFiles(addonsDir, Path.GetFileName(cached.Pbo) + ".*.bisign").Any()))
        {
            onLine?.Invoke($"skip: no changes since last build ({cached.UpdatedUtc:u})");
            return (stateHash, cache, new BuildResult(true, modName, buildDir, cached.Pbo, false,
                "skipped — no changes since last build (use force to rebuild)", "", "", preflight));
        }
        return (stateHash, cache, null);
    }

    // Junctions are anchored on the always-live work-drive source folder so they survive P: unmounts:
    // the source one lets AddonBuilder read via P:\<Mod>, the build one surfaces output at P:\Mods\@<Mod>.
    private static BuildResult? EnsureJunctions(string modName, string? workDriveSource, string projectDir, string root, PreflightView? preflight)
    {
        var srcJunction = ProjectPaths.JunctionPath(workDriveSource, modName);
        var srcEns = Junction.Ensure(srcJunction, projectDir);
        if (!srcEns.Ok)
            return FailResult(modName, preflight, $"source junction {srcJunction} → {projectDir} failed: {srcEns.Detail}");

        // One junction for the whole build area surfaces every build at P:\Mods\@<Mod>.
        var buildArea = ProjectPaths.BuildAreaJunction(workDriveSource);
        var buildEns = Junction.Ensure(buildArea, ProjectPaths.BuildRoot(root));
        if (!buildEns.Ok)
            return FailResult(modName, preflight, $"build junction {buildArea} → {ProjectPaths.BuildRoot(root)} failed: {buildEns.Detail}");

        return null;
    }

    private (string pbo, BuildResult? fail) PublishAndRecord(
        string modName, string root, string projectDir, string workDir, string workAddons, string addonsDir,
        string workPbo, DateTime startUtc, string stateHash, Dictionary<string, BuildCache.Entry> cache,
        string packOutput, PreflightView? preflight)
    {
        var (pubOk, pubDetail) = ModBuild.PublishAtomically(workAddons, addonsDir);
        if (!pubOk)
            return ("", FailResult(modName, preflight, $"publish failed: {pubDetail}", packOutput));
        try { Directory.Delete(workDir, recursive: true); } catch { /* leftover work dir is harmless */ }

        var pbo = Path.Combine(addonsDir, Path.GetFileName(workPbo));
        ModBuild.WriteMarker(ProjectPaths.BuildMarkerPath(root, modName), $"dzl-built {startUtc:O} from {projectDir}");

        cache[modName] = new BuildCache.Entry(stateHash, pbo, DateTime.UtcNow);
        BuildCache.Save(ConfigDir, cache);
        return (pbo, null);
    }

    // The public key goes to the built mod's keys\ (sibling of Addons\, outside the PBO) so the
    // distributed @<Mod> carries it and servers can whitelist it. The private key stays put.
    private static void ShipPublicKey(DzlConfig cfg, string root, string modName, string effectiveKey)
    {
        try
        {
            var pub = ProjectPaths.PublicKey(root, cfg.KeysDir, effectiveKey);
            if (File.Exists(pub))
            {
                var buildKeys = ProjectPaths.BuildKeysDir(root, modName);
                Directory.CreateDirectory(buildKeys);
                File.Copy(pub, Path.Combine(buildKeys, effectiveKey + ".bikey"), overwrite: true);
            }
        }
        catch { /* best-effort; the .bisign is already produced */ }
    }

    // Registers by the engine path P:\Mods\@<Mod> (not the physical dir) so a P:\Mods scan matches
    // it and Merge dedupes.
    private (bool registered, string note) RegisterInRunList(DzlConfig cfg, string active, string modName)
    {
        var updated = ModBuild.Register(cfg, ProjectPaths.BuildLink(modName));
        var registered = !ReferenceEquals(updated, cfg);
        if (registered)
            Profiles.Save(updated, string.IsNullOrEmpty(active) ? "default" : active, _configPath);

        var note = registered ? $"built + added to run-list ({active})" : "built (already in run-list)";
        return (registered, note);
    }
}
