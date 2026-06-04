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
