using System.Text.RegularExpressions;

namespace Dzl.Core.Config;

public static partial class Profiles
{
    [GeneratedRegex("[^A-Za-z0-9_.-]+")] private static partial Regex Unsafe();
    private static string Safe(string n) { var s = Unsafe().Replace(n.Trim(), "_"); return s.Length == 0 ? "preset" : s; }

    public static string PresetsDir(string configPath) => Path.Combine(Path.GetDirectoryName(configPath)!, "presets");
    public static string PresetFile(string name, string configPath) => Path.Combine(PresetsDir(configPath), Safe(name) + ".json");

    public static List<string> List(string configPath)
    {
        var d = PresetsDir(configPath);
        return Directory.Exists(d)
            ? Directory.GetFiles(d, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(x => x).ToList()
            : new();
    }

    public static void Save(DzlConfig cfg, string name, string configPath)
        => ConfigStore.Save(cfg, PresetFile(name, configPath));

    public static DzlConfig Load(string name, string configPath)
    {
        var f = PresetFile(name, configPath);
        if (!File.Exists(f)) throw new FileNotFoundException(name);
        return ConfigStore.Load(f);
    }

    public static bool Delete(string name, string configPath)
    {
        var f = PresetFile(name, configPath);
        if (!File.Exists(f)) return false;
        File.Delete(f); return true;
    }

    public static void SetActive(string name, string configPath)
    {
        var baseCfg = ConfigStore.Load(configPath) with { ActivePreset = name ?? "" };
        ConfigStore.Save(baseCfg, configPath);
    }

    public static (DzlConfig cfg, string savePath, string active) ResolveActive(string configPath)
    {
        var baseCfg = ConfigStore.Load(configPath);
        var name = baseCfg.ActivePreset;
        if (!string.IsNullOrEmpty(name))
        {
            var pf = PresetFile(name, configPath);
            if (File.Exists(pf)) return (ConfigStore.Load(pf), pf, name);
        }
        return (baseCfg, configPath, "");
    }

    public static string EnsureDefault(string configPath)
    {
        var baseCfg = ConfigStore.Load(configPath);
        if (!string.IsNullOrEmpty(baseCfg.ActivePreset) || List(configPath).Count > 0)
            return baseCfg.ActivePreset;
        Save(baseCfg, "default", configPath);
        ConfigStore.Save(baseCfg with { ActivePreset = "default" }, configPath);
        return "default";
    }
}
