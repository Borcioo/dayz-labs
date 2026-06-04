using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dzl.Core.Config;

/// <summary>
/// Manages server <b>instances</b> (formerly "presets/profiles"). After the SP8 config split, an
/// instance is the per-server <see cref="InstanceConfig"/> stored at <c>instances/&lt;name&gt;.json</c>;
/// machine-global settings live once in <c>config.json</c> (<see cref="GlobalConfig"/> via
/// <see cref="GlobalStore"/>). <see cref="ResolveActive"/> composes the two into the runtime
/// <see cref="DzlConfig"/>. Public method names are kept stable; semantics are now two-tier.
/// </summary>
public static partial class Profiles
{
    [GeneratedRegex("[^A-Za-z0-9_.-]+")] private static partial Regex Unsafe();
    private static string Safe(string n) { var s = Unsafe().Replace(n.Trim(), "_"); return s.Length == 0 ? "instance" : s; }

    /// <summary>Directory holding the per-server instance files (was <c>presets/</c>).</summary>
    public static string PresetsDir(string configPath) => Path.Combine(Path.GetDirectoryName(configPath)!, "instances");
    public static string PresetFile(string name, string configPath) => Path.Combine(PresetsDir(configPath), Safe(name) + ".json");

    public static List<string> List(string configPath)
    {
        var d = PresetsDir(configPath);
        return Directory.Exists(d)
            ? Directory.GetFiles(d, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(x => x).ToList()
            : new();
    }

    /// <summary>Persist the per-server slice of <paramref name="cfg"/> as <c>instances/&lt;name&gt;.json</c>.</summary>
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

    public static bool Delete(string name, string configPath)
    {
        var f = PresetFile(name, configPath);
        if (!File.Exists(f)) return false;
        File.Delete(f); return true;
    }

    /// <summary>Set the active instance (stored on <see cref="GlobalConfig.ActiveInstance"/>).</summary>
    public static void SetActive(string name, string configPath)
        => GlobalStore.Save(GlobalStore.Load(configPath) with { ActiveInstance = name ?? "" }, configPath);

    /// <summary>
    /// Compose global + the active instance into the runtime config. <c>savePath</c> is the active
    /// instance file (where per-server edits persist via <see cref="Save"/>); global edits go to
    /// <c>configPath</c> via <see cref="GlobalStore"/>. Runs <see cref="Migrate"/> first.
    /// </summary>
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
        // No active instance, or a dangling pointer (active names a missing file): treat as no-active —
        // compose global + defaults, report active="", and point a save at the default instance file.
        return (DzlConfig.Compose(g, new InstanceConfig()), PresetFile("default", configPath), "");
    }

    /// <summary>Ensure at least one instance exists + an active one is set. Returns the active name.</summary>
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

    /// <summary>
    /// One-time, idempotent migration from the legacy single-config (+ <c>presets/</c>) layout to
    /// the two-tier global + <c>instances/</c> layout. Detects a legacy <c>config.json</c> by its
    /// per-server keys at root (<c>mods</c>/<c>port</c>). Non-destructive: leaves <c>presets/</c> in place.
    /// </summary>
    public static void Migrate(string configPath)
    {
        if (Directory.Exists(PresetsDir(configPath))) return;     // already migrated (or fresh → EnsureDefault seeds)
        if (!File.Exists(configPath)) return;                     // fresh install, nothing to migrate

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

        var legacyCfg = ConfigStore.Load(configPath);             // full DzlConfig (legacy)
        Directory.CreateDirectory(PresetsDir(configPath));

        var legacyPresets = Path.Combine(Path.GetDirectoryName(configPath)!, "presets");
        if (Directory.Exists(legacyPresets))
        {
            foreach (var f in Directory.GetFiles(legacyPresets, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                try { Save(ConfigStore.Load(f), name, configPath); } catch { /* skip unreadable */ }
            }
        }
        if (!File.Exists(PresetFile("default", configPath)))
            Save(legacyCfg, "default", configPath);

        var active = !string.IsNullOrEmpty(legacyActive) ? legacyActive! : "default";
        if (!File.Exists(PresetFile(active, configPath))) active = "default";
        GlobalStore.Save(legacyCfg.GlobalPart(active), configPath);
    }
}
