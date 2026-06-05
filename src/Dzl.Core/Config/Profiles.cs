using System.Text.Json;
using System.Text.RegularExpressions;
using Dzl.Core.Projects;

namespace Dzl.Core.Config;

/// <summary>
/// Manages server <b>instances</b>. Each instance's dzl config lives <b>inside its own folder</b> at
/// <c>&lt;ProjectsRoot&gt;\servers\&lt;name&gt;\.dzl\instance.json</c> (next to its serverDZ.cfg / mpmissions /
/// profiles), so a server is a self-contained, portable, git-friendly unit. The single machine-global
/// config (<see cref="GlobalConfig"/> in <c>config.json</c>) holds only env paths + the active instance
/// name. <see cref="ResolveActive"/> composes global + the active instance into the runtime
/// <see cref="DzlConfig"/>. Public method names are kept stable across the storage evolutions.
/// </summary>
public static partial class Profiles
{
    [GeneratedRegex("[^A-Za-z0-9_.-]+")] private static partial Regex Unsafe();
    private static string Safe(string n) { var s = Unsafe().Replace(n.Trim(), "_"); return s.Length == 0 ? "instance" : s; }

    private const string DzlDir = ".dzl";
    private const string InstanceFileName = "instance.json";

    private static string Root(string configPath) =>
        ProjectPaths.Root(GlobalStore.Load(configPath).ProjectsRoot,
                          Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary><c>&lt;ProjectsRoot&gt;\servers</c> — the home for all server instance folders.</summary>
    public static string PresetsDir(string configPath) => ProjectPaths.ServersDir(Root(configPath));

    /// <summary>The instance's own folder, <c>&lt;ProjectsRoot&gt;\servers\&lt;name&gt;</c>.</summary>
    public static string InstanceDir(string name, string configPath) => Path.Combine(PresetsDir(configPath), Safe(name));

    /// <summary>The instance's dzl config file, <c>&lt;instance&gt;\.dzl\instance.json</c>.</summary>
    public static string PresetFile(string name, string configPath) => Path.Combine(InstanceDir(name, configPath), DzlDir, InstanceFileName);

    public static List<string> List(string configPath)
    {
        var dir = PresetsDir(configPath);
        if (!Directory.Exists(dir)) return new();
        return Directory.GetDirectories(dir)
            .Where(d => File.Exists(Path.Combine(d, DzlDir, InstanceFileName)))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(x => x).ToList();
    }

    /// <summary>Persist the per-server slice of <paramref name="cfg"/> into the instance's <c>.dzl</c> folder.</summary>
    public static void Save(DzlConfig cfg, string name, string configPath)
    {
        var f = PresetFile(name, configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, JsonSerializer.Serialize(cfg.InstancePart(), ConfigStore.Json));
    }

    /// <summary>Compose the global config + the named instance into a runtime <see cref="DzlConfig"/>.</summary>
    public static DzlConfig Load(string name, string configPath)
    {
        var f = PresetFile(name, configPath);
        if (!File.Exists(f)) throw new FileNotFoundException(name);
        var inst = JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(f), ConfigStore.Json) ?? new InstanceConfig();
        return DzlConfig.Compose(GlobalStore.Load(configPath), inst);
    }

    /// <summary>Remove the instance's dzl config (and its now-empty <c>.dzl</c> dir). The server's own
    /// files (serverDZ.cfg / mpmissions / profiles) are left on disk.</summary>
    public static bool Delete(string name, string configPath)
    {
        var f = PresetFile(name, configPath);
        if (!File.Exists(f)) return false;
        File.Delete(f);
        try
        {
            var d = Path.GetDirectoryName(f)!;
            if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
        catch { /* best-effort */ }
        return true;
    }

    public static void SetActive(string name, string configPath)
        => GlobalStore.Save(GlobalStore.Load(configPath) with { ActiveInstance = name ?? "" }, configPath);

    public static (DzlConfig cfg, string savePath, string active) ResolveActive(string configPath)
    {
        Migrate(configPath);
        var g = GlobalStore.Load(configPath);
        var name = g.ActiveInstance;
        if (!string.IsNullOrEmpty(name))
        {
            var pf = PresetFile(name, configPath);
            if (File.Exists(pf))
            {
                var inst = JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(pf), ConfigStore.Json) ?? new InstanceConfig();
                return (DzlConfig.Compose(g, inst), pf, name);
            }
        }
        return (DzlConfig.Compose(g, new InstanceConfig()), PresetFile("default", configPath), "");
    }

    public static string EnsureDefault(string configPath)
    {
        Migrate(configPath);
        var g = GlobalStore.Load(configPath);
        if (!string.IsNullOrEmpty(g.ActiveInstance) || List(configPath).Count > 0)
            return g.ActiveInstance;
        Save(DzlConfig.Compose(g, new InstanceConfig()), "default", configPath);
        GlobalStore.Save(g with { ActiveInstance = "default" }, configPath);
        return "default";
    }

    // ---- migration ------------------------------------------------------
    // Two one-time, idempotent, non-destructive steps run in order:
    //   1. legacy single-config + presets/  →  global config.json + flat instances/<name>.json
    //   2. flat instances/<name>.json       →  per-folder <ProjectsRoot>\servers\<name>\.dzl\instance.json
    public static void Migrate(string configPath)
    {
        MigrateLegacyToFlat(configPath);
        MigrateFlatToFolders(configPath);
    }

    private static string FlatInstancesDir(string configPath) =>
        Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "instances");

    private static void MigrateLegacyToFlat(string configPath)
    {
        var flat = FlatInstancesDir(configPath);
        if (Directory.Exists(flat)) return;        // already at (at least) the flat layout
        if (!File.Exists(configPath)) return;      // fresh install

        string raw;
        try { raw = File.ReadAllText(configPath); } catch { return; }

        bool legacy = false;
        string? legacyActive = null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                legacy = root.TryGetProperty("mods", out _) || root.TryGetProperty("port", out _);
                if (root.TryGetProperty("active_preset", out var ap) && ap.ValueKind == JsonValueKind.String)
                    legacyActive = ap.GetString();
            }
        }
        catch { return; }
        if (!legacy) return;

        var legacyCfg = ConfigStore.Load(configPath);
        Directory.CreateDirectory(flat);

        var legacyPresets = Path.Combine(Path.GetDirectoryName(configPath)!, "presets");
        if (Directory.Exists(legacyPresets))
        {
            foreach (var f in Directory.GetFiles(legacyPresets, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                try { File.WriteAllText(Path.Combine(flat, Safe(name) + ".json"),
                        JsonSerializer.Serialize(ConfigStore.Load(f).InstancePart(), ConfigStore.Json)); }
                catch { /* skip */ }
            }
        }
        var defaultFlat = Path.Combine(flat, "default.json");
        if (!File.Exists(defaultFlat))
            File.WriteAllText(defaultFlat, JsonSerializer.Serialize(legacyCfg.InstancePart(), ConfigStore.Json));

        var active = !string.IsNullOrEmpty(legacyActive) ? legacyActive! : "default";
        if (!File.Exists(Path.Combine(flat, Safe(active) + ".json"))) active = "default";
        GlobalStore.Save(legacyCfg.GlobalPart(active), configPath);
    }

    private static void MigrateFlatToFolders(string configPath)
    {
        var flat = FlatInstancesDir(configPath);
        if (!Directory.Exists(flat)) return;
        var files = Directory.GetFiles(flat, "*.json");
        if (files.Length == 0) return;

        // Detect ProjectsRoot from the instances' own folders when it isn't set, so discovery
        // (scan <ProjectsRoot>\servers\*) finds the servers where they actually live.
        var g = GlobalStore.Load(configPath);
        if (string.IsNullOrWhiteSpace(g.ProjectsRoot))
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var root = RootFromInstanceFile(f);
                if (root is not null) roots.Add(root);
            }
            if (roots.Count == 1)
            {
                g = g with { ProjectsRoot = roots.First() };
                GlobalStore.Save(g, configPath);
            }
        }

        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            try
            {
                var inst = JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(f), ConfigStore.Json) ?? new InstanceConfig();
                // Prefer the instance's own folder (next to its serverDZ.cfg) when it has an absolute path.
                var dir = Path.IsPathRooted(inst.ConfigName)
                    ? Path.GetDirectoryName(Path.GetFullPath(inst.ConfigName))!
                    : InstanceDir(name, configPath);
                var dst = Path.Combine(dir, DzlDir, InstanceFileName);
                if (File.Exists(dst)) continue;     // already migrated
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.WriteAllText(dst, JsonSerializer.Serialize(inst, ConfigStore.Json));
            }
            catch { /* skip unreadable */ }
        }
    }

    // <root>\servers\<name>\serverDZ.cfg  →  <root>
    private static string? RootFromInstanceFile(string flatFile)
    {
        try
        {
            var inst = JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(flatFile), ConfigStore.Json);
            if (inst is null || !Path.IsPathRooted(inst.ConfigName)) return null;
            var instDir = Path.GetDirectoryName(Path.GetFullPath(inst.ConfigName));     // <root>\servers\<name>
            var serversDir = Path.GetDirectoryName(instDir);                            // <root>\servers
            var root = Path.GetDirectoryName(serversDir);                               // <root>
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch { return null; }
    }
}
