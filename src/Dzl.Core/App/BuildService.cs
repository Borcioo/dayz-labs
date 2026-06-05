using Dzl.Core.Build;
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

    /// <summary>Build <paramref name="modName"/> and (on success) add it to the active run-list.</summary>
    /// <param name="clean">Pass <c>-clear</c> to AddonBuilder (wipe the temp/output first).</param>
    /// <param name="binarize">Binarize configs/models (AddonBuilder default). <c>false</c> = <c>-packonly</c>.</param>
    /// <param name="onLine">Optional live-log sink for each AddonBuilder output line.</param>
    /// <param name="sign">Sign the PBO with the creator's key (AddonBuilder <c>-sign</c>); copies the public
    /// <c>.bikey</c> into the mod's <c>keys\</c> so it ships. Fails if no key exists (generate one first).</param>
    public BuildResult Build(string modName, bool clean = false, bool binarize = true, bool sign = false, Action<string>? onLine = null)
    {
        BuildResult Fail(string msg, string output = "") =>
            new(false, modName, "", "", false, msg, output);

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

        // Resolve the signing key up front so we fail before building if signing was asked for but no key exists.
        string? signKey = null;
        if (sign)
        {
            var keyName = KeyName(cfg);
            if (keyName.Length == 0)
                return Fail("sign requested but no signing-key name — set one in Settings");
            signKey = ProjectPaths.PrivateKey(root, cfg.KeysDir, keyName);
            if (!File.Exists(signKey))
                return Fail($"signing key '{keyName}' not found at {signKey} — generate it first");
        }

        var workDriveSource = EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath);
        var buildDir = ProjectPaths.BuildDir(root, modName);
        var addonsDir = ProjectPaths.BuildAddonsDir(root, modName);
        Directory.CreateDirectory(addonsDir);

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

        var startUtc = DateTime.UtcNow;
        var pack = AddonBuilder.Pack(exe.ExePath, ProjectPaths.WorkDriveLink(modName), addonsDir,
            clear: clean, packOnly: !binarize, prefix: null, signKey: signKey, onLine: onLine);

        if (!pack.Ok)
            return Fail($"AddonBuilder exited {pack.ExitCode}", pack.Output);

        if (!ModBuild.HasFreshPbo(addonsDir, startUtc))
            return Fail("AddonBuilder reported success but no fresh .pbo appeared", pack.Output);

        var pbo = ModBuild.NewestPbo(addonsDir)!.FullName;
        ModBuild.WriteMarker(ProjectPaths.BuildMarkerPath(root, modName), $"dzl-built {startUtc:O} from {projectDir}");

        // Ship the public key inside the mod so servers can whitelist it (private key stays in the keys folder).
        if (sign)
        {
            try
            {
                var keyName = KeyName(cfg);
                var pub = ProjectPaths.PublicKey(root, cfg.KeysDir, keyName);
                if (File.Exists(pub))
                {
                    var modKeys = ProjectPaths.ModKeysDir(root, modName);
                    Directory.CreateDirectory(modKeys);
                    File.Copy(pub, Path.Combine(modKeys, keyName + ".bikey"), overwrite: true);
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
        return new BuildResult(true, modName, buildDir, pbo, registered, note, pack.Output);
    }
}
