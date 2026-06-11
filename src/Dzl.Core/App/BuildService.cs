using Dzl.Core.Build;
using Dzl.Core.Build.Preflight;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Tools;

namespace Dzl.Core.App;

/// <summary>
/// SP2 build→deploy: turn a mod <i>project</i> (source at <c>&lt;ProjectsRoot&gt;\mods\&lt;Mod&gt;</c>,
/// reached via its <c>P:\&lt;Mod&gt;</c> junction) into a loadable PBO under
/// <c>&lt;ProjectsRoot&gt;\build\@&lt;Mod&gt;\Addons\</c> (physical; surfaced on P: as
/// <c>P:\Mods\@&lt;Mod&gt;</c> via a junction), then register that build into the active server's run-list
/// by its <c>P:\Mods\@&lt;Mod&gt;</c> engine path. One facade per frontend; pure bits live in <see cref="ModBuild"/>.
/// </summary>
public sealed class BuildService
{
    private readonly string _configPath;
    public BuildService(string configPath) { _configPath = configPath; }

    private string ConfigDir => Path.GetDirectoryName(_configPath) ?? ".";

    /// <summary>The creator's signing-key name: the explicit config value, else the cached author handle,
    /// else "" (no key configured). One key signs all of a creator's mods.</summary>
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
        var exe = ToolCatalog.Find(cfg.DayzToolsPath, "addonbuilder");

        var isProject = Directory.Exists(projectDir) && ModProjects.IsProject(projectDir);
        var haveTool = exe is not null && exe.Exists;
        var pMounted = WorkDrive.IsMounted();
        var ready = isProject && haveTool && pMounted;
        var msg = !isProject ? "not a mod project ($PBOPREFIX$ / config.cpp missing)"
                : !haveTool ? "AddonBuilder not found — set the DayZ Tools path"
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
        if (File.Exists(ProjectPaths.PrivateKey(root, cfg.KeysDir, name)))
            return new KeyResult(true, ProjectPaths.PrivateKey(root, cfg.KeysDir, name),
                ProjectPaths.PublicKey(root, cfg.KeysDir, name), "key already exists");

        var exe = ToolCatalog.Find(cfg.DayzToolsPath, "dscreatekey");
        if (exe is null || !exe.Exists)
            return new KeyResult(false, "", "", "DSCreateKey not found — check the DayZ Tools path");

        return DsTools.CreateKey(exe.ExePath, keysDir, name);
    }

    /// <summary>Run the preflight rule set over a mod project. Read-only on the project; writes
    /// only the report files. CfgConvert syntax gate engages automatically when DayZ Tools is
    /// configured; the work drive is used for vanilla-reference resolution only when mounted.</summary>
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
    public BuildResult Build(string modName, bool clean = false, bool binarize = true, bool sign = false, Action<string>? onLine = null, bool force = false, string? keyName = null)
    {
        PreflightView? preflight = null;   // set by the gate below; rides along on every result
        BuildResult Fail(string msg, string output = "") =>
            new(false, modName, "", "", false, msg, output,
                BuildDiagnostics.Format(BuildDiagnostics.Diagnose(output + "\n" + msg)), preflight);

        if (!ProjectPaths.IsValidName(modName))
            return Fail($"invalid mod name: {modName}");

        Profiles.EnsureDefault(_configPath);
        var (cfg, _, active) = Profiles.ResolveActive(_configPath);

        var root = ProjectPaths.Root(cfg);
        var projectDir = ProjectPaths.ModDir(root, modName);
        if (!Directory.Exists(projectDir) || !ModProjects.IsProject(projectDir))
            return Fail($"not a mod project: {projectDir} (need $PBOPREFIX$ or config.cpp)");

        var exe = ToolCatalog.Find(cfg.DayzToolsPath, "addonbuilder");
        if (exe is null || !exe.Exists)
            return Fail("AddonBuilder not found — set DayZ Tools path / install Addon Builder");

        if (!WorkDrive.IsMounted())
            return Fail("P: work drive not mounted — mount it first (binarize resolves vanilla data + includes against P:)");

        // Preflight gate: AddonBuilder reports "Build Successful" even for configs it silently
        // mangles, so error-severity findings block the build (config flag to opt out). The view
        // rides along on the result so frontends can show the findings without a second run.
        if (cfg.PreflightBeforeBuild)
        {
            onLine?.Invoke("preflight: checking project before build ...");
            preflight = Preflight(modName, saveReport: true);
            foreach (var f in preflight.Findings.Where(f => f.Severity == FindingSeverity.Error))
                onLine?.Invoke($"preflight ✗ {f.Rule}: {f.Message}");
            if (!preflight.Ok)
                return Fail(
                    $"preflight failed with {preflight.Errors} error(s) — fix them or set preflight_before_build=false to bypass",
                    string.Join("\n", preflight.Findings
                        .Where(f => f.Severity == FindingSeverity.Error)
                        .Select(f => $"{f.Rule}: {f.Message}" + (f.File.Length > 0 ? $"  [{f.File}:{f.Line}]" : ""))));
            onLine?.Invoke($"preflight: ok ({preflight.Warnings} warning(s))");
        }

        // Resolve the signing key up front so we fail before building if signing was asked for but
        // no key exists. An explicit keyName overrides the configured/default key for this build.
        string? signKey = null;
        var effectiveKey = !string.IsNullOrWhiteSpace(keyName) ? keyName.Trim() : KeyName(cfg);
        if (sign)
        {
            if (effectiveKey.Length == 0)
                return Fail("sign requested but no signing-key name — set one in Settings");
            signKey = ProjectPaths.PrivateKey(root, cfg.KeysDir, effectiveKey);
            if (!File.Exists(signKey))
                return Fail($"signing key '{effectiveKey}' not found at {signKey} — generate it first");
            onLine?.Invoke($"signing with key: {effectiveKey}");
        }

        var workDriveSource = EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath);
        var buildDir = ProjectPaths.BuildDir(root, modName);
        var addonsDir = ProjectPaths.BuildAddonsDir(root, modName);
        Directory.CreateDirectory(addonsDir);

        // Skip-unchanged: same payload + same settings + output still present → nothing to do.
        var sha1Memo = new Dictionary<string, string>();
        var settingsFingerprint =
            $"binarize={binarize};sign={sign};" +
            $"exe={BuildCache.Fingerprint(exe.ExePath, sha1Memo)};" +
            $"key={(sign ? BuildCache.Fingerprint(signKey, sha1Memo) : "off")}";
        var stateHash = BuildCache.ComputeStateHash(projectDir,
            PreflightOptions.DefaultExcludes, settingsFingerprint, sha1Memo);
        var cache = BuildCache.Load(ConfigDir);
        if (!force && cache.TryGetValue(modName, out var cached) && cached.Hash == stateHash &&
            File.Exists(cached.Pbo) &&
            (!sign || Directory.EnumerateFiles(addonsDir, Path.GetFileName(cached.Pbo) + ".*.bisign").Any()))
        {
            onLine?.Invoke($"skip: no changes since last build ({cached.UpdatedUtc:u})");
            return new BuildResult(true, modName, buildDir, cached.Pbo, false,
                "skipped — no changes since last build (use force to rebuild)", "", "", preflight);
        }

        // Junctions anchored on the always-live work-drive source folder (survive P: unmounts): the source
        // so AddonBuilder reads it via P:\<Mod>, and the build so it surfaces at P:\Mods\@<Mod>. Both targets
        // live physically under ProjectsRoot (mods\ and build\).
        var srcJunction = ProjectPaths.JunctionPath(workDriveSource, modName);
        var srcEns = Junction.Ensure(srcJunction, projectDir);
        if (!srcEns.Ok)
            return Fail($"source junction {srcJunction} → {projectDir} failed: {srcEns.Detail}");

        // One junction for the whole build area surfaces every build at P:\Mods\@<Mod>.
        var buildArea = ProjectPaths.BuildAreaJunction(workDriveSource);
        var buildEns = Junction.Ensure(buildArea, ProjectPaths.BuildRoot(root));
        if (!buildEns.Ok)
            return Fail($"build junction {buildArea} → {ProjectPaths.BuildRoot(root)} failed: {buildEns.Detail}");

        // AddonBuilder writes into a work dir; the loadable Addons\ is only touched after the
        // output verifies, and then atomically (backup → swap → rollback on failure).
        var workDir = Path.Combine(buildDir, ".work");
        var workAddons = Path.Combine(workDir, "Addons");
        if (Directory.Exists(workAddons)) try { Directory.Delete(workAddons, recursive: true); } catch { }
        Directory.CreateDirectory(workAddons);
        var abTemp = Path.Combine(workDir, "temp");
        Directory.CreateDirectory(abTemp);   // AddonBuilder fails at "Syncing folders" when -temp= doesn't exist
        var includeFile = AddonBuilder.WriteIncludeFile(workDir);

        var startUtc = DateTime.UtcNow;
        var pack = AddonBuilder.Pack(exe.ExePath, ProjectPaths.WorkDriveLink(modName), workAddons,
            clear: clean, packOnly: !binarize, prefix: null, signKey: signKey, onLine: onLine,
            tempDir: abTemp, includeFile: includeFile);

        if (!pack.Ok)
            return Fail($"AddonBuilder exited {pack.ExitCode}", pack.Output);

        if (!ModBuild.HasFreshPbo(workAddons, startUtc))
            return Fail("AddonBuilder reported success but no fresh .pbo appeared — check the log above for the real error",
                pack.Output);

        var workPbo = ModBuild.NewestPbo(workAddons)!.FullName;

        // Post-pack verification: the packed prefix must match the project's $PBOPREFIX$ (a
        // mismatch means the mod loads with every asset path broken), and a signed build must
        // actually carry a signature (AddonBuilder -sign can silently not sign).
        var expectedPrefix = PathResolver.ReadPrefix(projectDir);
        var info = PboHeader.Read(workPbo);
        if (info is null)
            onLine?.Invoke("verify: could not parse the produced PBO header (skipping prefix check)");
        else if (expectedPrefix.Length > 0 && info.Prefix.Length > 0 &&
                 !string.Equals(info.Prefix, expectedPrefix, StringComparison.OrdinalIgnoreCase))
            return Fail($"packed PBO prefix '{info.Prefix}' does not match $PBOPREFIX$ '{expectedPrefix}' — assets would resolve to the wrong paths", pack.Output);

        if (sign && PboHeader.FindSignature(workPbo) is null)
            return Fail("signing was requested but no .bisign was produced next to the PBO", pack.Output);

        var (pubOk, pubDetail) = ModBuild.PublishAtomically(workAddons, addonsDir);
        if (!pubOk)
            return Fail($"publish failed: {pubDetail}", pack.Output);
        try { Directory.Delete(workDir, recursive: true); } catch { /* leftover work dir is harmless */ }

        var pbo = Path.Combine(addonsDir, Path.GetFileName(workPbo));
        ModBuild.WriteMarker(ProjectPaths.BuildMarkerPath(root, modName), $"dzl-built {startUtc:O} from {projectDir}");

        cache[modName] = new BuildCache.Entry(stateHash, pbo, DateTime.UtcNow);
        BuildCache.Save(ConfigDir, cache);

        // Place the public key in the built mod's keys\ (sibling of Addons\, outside the PBO) so the
        // distributed/loaded @<Mod> carries it and servers can whitelist it. The private key stays put.
        if (sign)
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

        // Register by the engine/toolchain path P:\Mods\@<Mod> (the build is surfaced there via the build-area
        // junction). Clean + conventional ("Mods is on P:"), and it matches a P:\Mods scan so Merge dedupes.
        var updated = ModBuild.Register(cfg, ProjectPaths.BuildLink(modName));
        var registered = !ReferenceEquals(updated, cfg);
        if (registered)
            Profiles.Save(updated, string.IsNullOrEmpty(active) ? "default" : active, _configPath);

        var note = registered ? $"built + added to run-list ({active})" : "built (already in run-list)";
        return new BuildResult(true, modName, buildDir, pbo, registered, note, pack.Output, "", preflight);
    }
}
