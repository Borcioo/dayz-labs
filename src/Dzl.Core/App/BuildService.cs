using Dzl.Core.Build;
using Dzl.Core.Config;
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

        // The engine + AddonBuilder see the source on P:; (re)create the junction if missing/stale.
        var link = ProjectPaths.WorkDriveLink(modName);
        var ens = Junction.Ensure(link, projectDir);
        if (!ens.Ok)
            return Fail($"junction P:\\{modName} → {projectDir} failed: {ens.Detail}");

        var addonsDir = ModBuild.AddonsDir(modName);
        Directory.CreateDirectory(addonsDir);

        var startUtc = DateTime.UtcNow;
        var pack = AddonBuilder.Pack(exe.ExePath, link, addonsDir,
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
