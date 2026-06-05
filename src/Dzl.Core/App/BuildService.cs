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
/// by its always-live physical path. One facade per frontend; pure bits live in <see cref="ModBuild"/>.
/// </summary>
public sealed class BuildService
{
    private readonly string _configPath;
    public BuildService(string configPath) { _configPath = configPath; }

    /// <summary>Resolved, read-only preview of where a build will read from / write to and which tool it
    /// will use — so a UI can pre-fill the paths and warn before running. No side effects.</summary>
    public sealed record BuildPlanView(
        bool Ok, string Mod, string ProjectDir, string SourceOnP, string OutputDir, string AddonsDir,
        string AddonBuilderExe, bool WorkDriveMounted, bool Ready, string Message);

    public BuildPlanView Plan(string mod)
    {
        if (!ProjectPaths.IsValidName(mod))
            return new BuildPlanView(false, mod, "", "", "", "", "", false, false, $"invalid mod name: {mod}");

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

        return new BuildPlanView(true, mod, projectDir, src,
            ProjectPaths.BuildDir(root, mod), ProjectPaths.BuildAddonsDir(root, mod),
            exe?.ExePath ?? "(not found)", pMounted, ready, msg);
    }

    /// <summary>Build <paramref name="modName"/> and (on success) add it to the active run-list.</summary>
    /// <param name="clean">Pass <c>-clear</c> to AddonBuilder (wipe the temp/output first).</param>
    /// <param name="binarize">Binarize configs/models (AddonBuilder default). <c>false</c> = <c>-packonly</c>.</param>
    /// <param name="onLine">Optional live-log sink for each AddonBuilder output line.</param>
    public BuildResult Build(string modName, bool clean = false, bool binarize = true, Action<string>? onLine = null)
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
            clear: clean, packOnly: !binarize, prefix: null, signKey: null, onLine: onLine);

        if (!pack.Ok)
            return Fail($"AddonBuilder exited {pack.ExitCode}", pack.Output);

        if (!ModBuild.HasFreshPbo(addonsDir, startUtc))
            return Fail("AddonBuilder reported success but no fresh .pbo appeared", pack.Output);

        var pbo = ModBuild.NewestPbo(addonsDir)!.FullName;
        ModBuild.WriteMarker(ProjectPaths.BuildMarkerPath(root, modName), $"dzl-built {startUtc:O} from {projectDir}");

        // Register the physical build folder (always-live) into the active run-list (idempotent).
        var updated = ModBuild.Register(cfg, buildDir);
        var registered = !ReferenceEquals(updated, cfg);
        if (registered)
            Profiles.Save(updated, string.IsNullOrEmpty(active) ? "default" : active, _configPath);

        var note = registered ? $"built + added to run-list ({active})" : "built (already in run-list)";
        return new BuildResult(true, modName, buildDir, pbo, registered, note, pack.Output);
    }
}
