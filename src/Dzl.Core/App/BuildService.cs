using Dzl.Core.Build;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Tools;

namespace Dzl.Core.App;

/// <summary>
/// SP2 build→deploy: turn a mod <i>project</i> (source under ProjectsRoot, reachable via its
/// <c>P:\&lt;Mod&gt;\</c> junction) into a loadable PBO under <c>P:\Mods\@&lt;Mod&gt;\Addons\</c>,
/// then register that <c>@&lt;Mod&gt;</c> into the active server instance's run-list. One facade per
/// frontend (CLI/MCP/tray); orchestration only — pure bits live in <see cref="ModBuild"/>.
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
        var projectDir = ProjectPaths.ModDir(ProjectPaths.Root(cfg), mod);
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
            ModBuild.OutputDir(mod), ModBuild.AddonsDir(mod), exe?.ExePath ?? "(not found)", pMounted, ready, msg);
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

        // (Re)create the junction on the always-live work-drive source folder (survives P: unmounts);
        // P: is mounted here (checked above), so AddonBuilder reads the same object via the P:\ path.
        var junction = ProjectPaths.JunctionPath(EnvDetect.WorkDir(cfg.DayzToolsPath), modName);
        var ens = Junction.Ensure(junction, projectDir);
        if (!ens.Ok)
            return Fail($"junction {junction} → {projectDir} failed: {ens.Detail}");

        var addonsDir = ModBuild.AddonsDir(modName);
        Directory.CreateDirectory(addonsDir);

        var startUtc = DateTime.UtcNow;
        var pack = AddonBuilder.Pack(exe.ExePath, ProjectPaths.WorkDriveLink(modName), addonsDir,
            clear: clean, packOnly: !binarize, prefix: null, signKey: null, onLine: onLine);

        if (!pack.Ok)
            return Fail($"AddonBuilder exited {pack.ExitCode}", pack.Output);

        if (!ModBuild.HasFreshPbo(addonsDir, startUtc))
            return Fail("AddonBuilder reported success but no fresh .pbo appeared", pack.Output);

        var pbo = ModBuild.NewestPbo(addonsDir)!.FullName;
        ModBuild.WriteMarker(modName, $"dzl-built {startUtc:O} from {projectDir}");

        // Register into the active instance's run-list (idempotent), saving to the active instance.
        var updated = ModBuild.Register(cfg, ModBuild.LoadPath(modName));
        var registered = !ReferenceEquals(updated, cfg);
        if (registered)
            Profiles.Save(updated, string.IsNullOrEmpty(active) ? "default" : active, _configPath);

        var note = registered ? $"built + added to run-list ({active})" : "built (already in run-list)";
        return new BuildResult(true, modName, ModBuild.OutputDir(modName), pbo, registered, note, pack.Output);
    }
}
