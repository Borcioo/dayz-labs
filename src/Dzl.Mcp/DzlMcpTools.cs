using System.ComponentModel;
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Tools;
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
        => J(Svc().Start(mode, client, "mcp"));

    [McpServerTool, Description("Stop the server (and optionally the client).")]
    public static string Stop([Description("also stop the client")] bool client = false) => J(Svc().Stop(client, "mcp"));

    [McpServerTool, Description("Restart the server. mode = debug|normal.")]
    public static string Restart([Description("debug|normal")] string mode = "debug") => J(Svc().Restart(mode, "mcp"));

    // --- DayZ Tools ---

    private static string ToolsPath() => Profiles.ResolveActive(ConfigPath()).cfg.DayzToolsPath;

    [McpServerTool, Description("Discover DayZ Tools exes under <DayZ Tools>\\Bin (key, name, path, present, kind).")]
    public static string ListTools() => J(ToolCatalog.Discover(ToolsPath()));

    [McpServerTool, Description("Launch a DayZ tool GUI by key (see list_tools).")]
    public static string OpenTool([Description("Tool key, e.g. workbench")] string key)
    {
        var tool = ToolCatalog.Find(ToolsPath(), key);
        if (tool is null || !tool.Exists) return J(new { ok = false, error = $"tool not found: {key}" });
        return J(new { ok = ToolLauncher.Launch(tool) });
    }

    [McpServerTool, Description("Batch convert PNG/TGA to PAA in a folder (ImageToPAA).")]
    public static string ConvertPaa([Description("Folder with images")] string dir,
                                    [Description("recurse into subfolders")] bool recursive = false)
    {
        var exe = ToolCatalog.Find(ToolsPath(), "imagetopaa");
        if (exe is null || !exe.Exists) return J(new { ok = false, error = "tool not found: imagetopaa" });
        return J(ImageToPaa.ConvertFolder(exe.ExePath, dir, recursive));
    }

    [McpServerTool, Description("Build a mod project into a PBO (Addon Builder) and add the @<Mod> to the active server's run-list. Higher-level than pack_pbo: resolves the project under ProjectsRoot, ensures the P: junction, deploys to P:\\Mods\\@<Mod>\\Addons and registers it.")]
    public static string BuildMod([Description("Mod project name (under ProjectsRoot)")] string mod,
                                  [Description("Wipe output first (AddonBuilder -clear)")] bool clean = false,
                                  [Description("Binarize configs/models (false = -packonly)")] bool binarize = true,
                                  [Description("Sign the PBO with your signing key (must exist — see generate_key)")] bool sign = false,
                                  [Description("Rebuild even when nothing changed (skip-unchanged cache)")] bool force = false)
        => J(new BuildService(ConfigPath()).Build(mod, clean, binarize, sign, force: force));

    [McpServerTool, Description("Preflight a mod project before building: config sanity (CfgPatches/CfgMods/CfgConvert syntax gate), missing/excluded asset references, baked absolute paths, path hygiene (lowercase rule, case conflicts), texture freshness, ODOL p3ds, Enforce-script traps. Returns findings with rule ids, file and line. ok=false means error-severity findings exist.")]
    public static string Preflight([Description("Mod project name (under ProjectsRoot)")] string mod)
        => J(new BuildService(ConfigPath()).Preflight(mod));

    [McpServerTool, Description("Create the creator's signing key pair (DSCreateKey) in the keys folder. One key signs all your mods. Name defaults to the configured signing key / author.")]
    public static string GenerateKey([Description("Key name (optional; defaults to configured signing key / author)")] string? name = null)
        => J(new BuildService(ConfigPath()).GenerateKey(name));

    [McpServerTool, Description("Pack a source folder into a PBO (Addon Builder).")]
    public static string PackPbo([Description("Source folder")] string src,
                                 [Description("Output folder")] string dst,
                                 [Description("PBO prefix")] string? prefix = null,
                                 [Description("Private key file to sign with")] string? sign = null)
    {
        var exe = ToolCatalog.Find(ToolsPath(), "addonbuilder");
        if (exe is null || !exe.Exists) return J(new { ok = false, error = "tool not found: addonbuilder" });
        return J(AddonBuilder.Pack(exe.ExePath, src, dst, true, true, prefix, sign));
    }

    [McpServerTool, Description("Unbinarize a config.bin to .cpp (CfgConvert / DeRap).")]
    public static string Unbinarize([Description("config.bin path")] string bin,
                                    [Description("output .cpp (defaults to same name)")] string? outCpp = null)
    {
        var exe = ToolCatalog.Find(ToolsPath(), "cfgconvert");
        if (exe is null || !exe.Exists) return J(new { ok = false, error = "tool not found: cfgconvert" });
        var (ok, output) = CfgConvert.Unbinarize(exe.ExePath, bin, outCpp ?? Path.ChangeExtension(bin, ".cpp"));
        return J(new { ok, output });
    }

    // --- GitHub (SP4) ---

    [McpServerTool, Description("Git status of a mod project: IsRepo, Branch, Ahead, Behind, Dirty, HasRemote, Detail.")]
    public static string RepoStatus([Description("Mod project name")] string mod)
        => J(new RepoService(ConfigPath()).Status(mod));

    [McpServerTool, Description("Changed files in a mod's git repo (staged + unstaged + untracked): Path, Index/Worktree status, Staged, Untracked.")]
    public static string GitChanges([Description("Mod project name")] string mod)
    {
        var (ok, error, files) = new RepoService(ConfigPath()).Changes(mod);
        return J(new { ok, error, files });
    }

    [McpServerTool, Description("Recent commits in a mod's git repo (newest first): Hash, Author, Date, Subject.")]
    public static string GitLog([Description("Mod project name")] string mod,
                                [Description("how many commits")] int count = 20)
    {
        var (ok, error, commits) = new RepoService(ConfigPath()).Log(mod, count);
        return J(new { ok, error, commits });
    }

    [McpServerTool, Description("Diff of a mod's work tree vs HEAD (unified). Optionally limit to one file path.")]
    public static string GitDiff([Description("Mod project name")] string mod,
                                 [Description("file path within the mod (omit = whole repo)")] string? file = null)
    {
        var (ok, error, diff) = new RepoService(ConfigPath()).Diff(mod, file);
        return J(new { ok, error, diff });
    }

    [McpServerTool, Description("Stage and commit a mod's changes. all=true stages everything first; all=false commits only the staged index.")]
    public static string GitCommit([Description("Mod project name")] string mod,
                                   [Description("commit message")] string message,
                                   [Description("stage everything first")] bool all = true)
        => J(new RepoService(ConfigPath()).Commit(mod, message, all));

    [McpServerTool, Description("List a mod's local git branches + the current one.")]
    public static string GitBranches([Description("Mod project name")] string mod)
    {
        var (ok, error, current, branches) = new RepoService(ConfigPath()).Branches(mod);
        return J(new { ok, error, current, branches });
    }

    [McpServerTool, Description("Check out an existing branch in a mod's repo.")]
    public static string GitCheckout([Description("Mod project name")] string mod,
                                     [Description("branch name")] string branch)
        => J(new RepoService(ConfigPath()).Checkout(mod, branch));

    [McpServerTool, Description("Create and switch to a new branch in a mod's repo.")]
    public static string GitCreateBranch([Description("Mod project name")] string mod,
                                         [Description("new branch name")] string name)
        => J(new RepoService(ConfigPath()).CreateBranch(mod, name));

    [McpServerTool, Description("Push the current branch of a mod's repo (sets upstream if missing). Needs a remote.")]
    public static string GitPush([Description("Mod project name")] string mod)
        => J(new RepoService(ConfigPath()).Push(mod));

    [McpServerTool, Description("Pull the current branch of a mod's repo. Needs a remote.")]
    public static string GitPull([Description("Mod project name")] string mod)
        => J(new RepoService(ConfigPath()).Pull(mod));

    [McpServerTool, Description("Init git (with .gitignore + first commit) and create & push a GitHub repo named after the mod.")]
    public static string CreateRepo([Description("Mod project name")] string mod,
                                    [Description("private repo (false = public)")] bool @private = true,
                                    [Description("repo description")] string? description = null)
        => J(new RepoService(ConfigPath()).Publish(mod, @private, description));

    [McpServerTool, Description("Cut a GitHub release at HEAD for the mod (creates + pushes the tag).")]
    public static string Release([Description("Mod project name")] string mod,
                                 [Description("tag, e.g. v1.0.0")] string tag,
                                 [Description("release notes (omit = auto-generated)")] string? notes = null)
        => J(new RepoService(ConfigPath()).Release(mod, tag, notes));

    // --- Steam Workshop (SP5) ---

    [McpServerTool, Description("Search the Steam Workshop for DayZ mods (needs a Steam Web API key in config). Returns id + title.")]
    public static async Task<string> WorkshopSearch([Description("search text")] string query,
                                                    [Description("max results")] int count = 20)
    {
        var (ok, error, items) = await new WorkshopService(ConfigPath()).SearchAsync(query, count);
        return J(new { ok, error, items });
    }

    [McpServerTool, Description("Download a Workshop item by id via steamcmd (opens a console for Steam login/Guard). Needs steamcmd configured.")]
    public static string WorkshopAdd([Description("Workshop published-file id")] string id)
        => J(new WorkshopService(ConfigPath()).Download(id));

    [McpServerTool, Description("Re-download a Workshop item to update it (or all downloaded items when id omitted).")]
    public static string WorkshopUpdate([Description("item id (optional; omit = all)")] string? id = null)
    {
        var svc = new WorkshopService(ConfigPath());
        var ids = id is not null ? new List<string> { id } : svc.Downloaded();
        return J(ids.Select(x => svc.Download(x)).ToList());
    }

    // --- Central Economy (types.xml) (SP6) ---

    [McpServerTool, Description("List types from the active server mission's CE files. Filters: name (substring), source (vanilla|mod|custom), file (basename substring). Each entry includes source_file (basename) and origin in addition to name/nominal/min/lifetime/restock/cost/category/usage/value/tag.")]
    public static string TypesList([Description("name substring filter (optional)")] string? filter = null,
                                   [Description("origin filter: vanilla|mod|custom (optional)")] string? source = null,
                                   [Description("source file basename substring filter (optional)")] string? file = null)
    {
        var rows = new TypesService(ConfigPath()).Rows();
        if (filter is not null)
            rows = rows.Where(r => r.Entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (source is not null)
            rows = rows.Where(r => r.Origin.ToString().Equals(source, StringComparison.OrdinalIgnoreCase)).ToList();
        if (file is not null)
            rows = rows.Where(r => Path.GetFileName(r.Entry.SourceFile).Contains(file, StringComparison.OrdinalIgnoreCase)).ToList();
        return J(rows.Select(r => new
        {
            name       = r.Entry.Name,
            nominal    = r.Entry.Nominal,
            min        = r.Entry.Min,
            lifetime   = r.Entry.Lifetime,
            restock    = r.Entry.Restock,
            cost       = r.Entry.Cost,
            category   = r.Entry.Category,
            usage      = r.Entry.Usage,
            value      = r.Entry.Value,
            tag        = r.Entry.Tag,
            source_file = Path.GetFileName(r.Entry.SourceFile),
            origin     = r.Origin.ToString().ToLowerInvariant(),
        }).ToList());
    }

    [McpServerTool, Description("Lint the active mission's Central Economy: unknown usage/value/tag/category vs cfglimitsdefinition, duplicate type names, structural issues.")]
    public static string TypesLint()
    {
        var findings = new TypesService(ConfigPath()).Lint();
        return J(findings.Select(f => new
        {
            severity = f.Severity.ToString().ToLowerInvariant(),
            code     = f.Code,
            message  = f.Message,
            entry    = f.EntryName,
            file     = Path.GetFileName(f.File),
        }).ToList());
    }

    [McpServerTool, Description("Set/insert a type in the active mission's types.xml (only given fields change; versioned backup first).")]
    public static string TypesSet([Description("Type/class name")] string cls,
                                  [Description("nominal spawn count")] int? nominal = null,
                                  [Description("minimum before restock")] int? min = null,
                                  [Description("lifetime (despawn seconds)")] int? lifetime = null,
                                  [Description("restock seconds")] int? restock = null,
                                  [Description("spawn cost")] int? cost = null,
                                  [Description("category name")] string? category = null,
                                  [Description("target CE file basename (optional; used only when type is new)")] string? file = null)
        => J(new TypesService(ConfigPath()).Set(cls, nominal, min, lifetime, restock, cost, category, file));

    [McpServerTool, Description("Remove a type from the active mission's types.xml (versioned backup first).")]
    public static string TypesRemove([Description("Type/class name")] string cls)
        => J(new TypesService(ConfigPath()).Remove(cls));

    [McpServerTool, Description("List versioned backups of the active mission's types.xml (newest first).")]
    public static string TypesBackups() => J(new TypesService(ConfigPath()).Backups());

    [McpServerTool, Description("Restore a types.xml backup over the live file (snapshots the current file first).")]
    public static string TypesRestore([Description("Backup file path (from types_backups)")] string file)
        => J(new TypesService(ConfigPath()).Restore(file));

    // --- Server instances ---

    [McpServerTool, Description("Scaffold a new server instance and save it as a preset. Returns Ok, Name, Dir, Port, Message.")]
    public static string NewServer([Description("Instance name")] string name,
                                   [Description("Map name, e.g. chernarus or livonia")] string map = "chernarus",
                                   [Description("UDP port (auto-assigned if null)")] int? port = null)
        => J(new ServerService(ConfigPath()).Create(name, map, port));

    [McpServerTool, Description("List all scaffolded server instances (Name, Dir, CfgPath).")]
    public static string ListServers() => J(new ServerService(ConfigPath()).List());

    [McpServerTool, Description("Activate a server instance by name (switches the active preset).")]
    public static string UseServer([Description("Server instance / preset name")] string name)
        => J(Svc().SetPreset(name));

    [McpServerTool, Description("Check/mount/unmount the P: work drive. action = status|mount|unmount.")]
    public static string WorkDriveAction([Description("status|mount|unmount")] string action)
    {
        switch (action)
        {
            case "mount":
                var wdExe = Path.Combine(ToolsPath(), "Bin", "WorkDrive", "WorkDrive.exe");
                WorkDrive.Mount(File.Exists(wdExe) ? wdExe : "", EnvDetect.WorkDir(ToolsPath()));
                break;
            case "unmount":
                var wdExeOff = Path.Combine(ToolsPath(), "Bin", "WorkDrive", "WorkDrive.exe");
                WorkDrive.Unmount(File.Exists(wdExeOff) ? wdExeOff : "");
                break;
        }
        return J(new { mounted = WorkDrive.IsMounted() });
    }

    // --- Mod projects ---

    private static string ProjectsRoot() => ProjectPaths.Root(Profiles.ResolveActive(ConfigPath()).cfg);

    [McpServerTool, Description("Scaffold a new DayZ mod source project. Caches the author handle for future calls.")]
    public static string NewMod([Description("Mod name (letters/digits/underscores, start with letter)")] string name,
                                [Description("Author handle (cached when provided)")] string? author = null)
    {
        var configDir = Path.GetDirectoryName(ConfigPath())!;
        var resolvedAuthor = author ?? ModScaffold.CachedAuthor(configDir);
        if (resolvedAuthor is null)
            return J(new { ok = false, error = "no author" });
        var root = ProjectsRoot();
        var scaffold = ModScaffold.Scaffold(root, name, resolvedAuthor);
        if (author is not null) ModScaffold.SaveAuthor(configDir, author);
        var link = scaffold.Ok
            ? Junction.Ensure(ProjectPaths.WorkDriveLink(name), ProjectPaths.ModDir(root, name))
            : null;
        return J(new { scaffold, link });
    }

    [McpServerTool, Description("Import an existing mod source folder into ProjectsRoot and link it on P:.")]
    public static string ImportMod([Description("Path to the existing mod source folder")] string path,
                                   [Description("Override the mod name (defaults to folder name)")] string? name = null)
        => J(ModImport.Import(ProjectsRoot(), path, name));

    [McpServerTool, Description("Create or repair the P:\\ junction for a mod source project.")]
    public static string LinkMod([Description("Mod name")] string name)
    {
        var root = ProjectsRoot();
        return J(Junction.Ensure(ProjectPaths.WorkDriveLink(name), ProjectPaths.ModDir(root, name)));
    }

    [McpServerTool, Description("List mod source projects under ProjectsRoot with their P: link state.")]
    public static string ListModProjects() => J(ModProjects.Discover(ProjectsRoot()));
}
