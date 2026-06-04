using System.Text.Json;

namespace Dzl.Core.Config;

/// <summary>Loads/saves the machine-global <see cref="GlobalConfig"/> to <c>config.json</c>.
/// Snake_case JSON, unknown keys tolerated (so a legacy full-config file still yields its global
/// slice on read; migration rewrites it to the global-only shape).</summary>
public static class GlobalStore
{
    public static GlobalConfig Load(string configPath)
    {
        if (!File.Exists(configPath)) return new GlobalConfig();
        try { return JsonSerializer.Deserialize<GlobalConfig>(File.ReadAllText(configPath), ConfigStore.Json) ?? new GlobalConfig(); }
        catch (JsonException) { return new GlobalConfig(); }
    }

    public static void Save(GlobalConfig cfg, string configPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(cfg, ConfigStore.Json));
    }
}
