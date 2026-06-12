using System.Text.Json;
using Dzl.Core.Json;

namespace Dzl.Core.Config;

public static class ConfigStore
{
    /// <summary>Alias for <see cref="DzlJson.SnakeIndented"/> — kept so call sites don't churn.</summary>
    public static readonly JsonSerializerOptions Json = DzlJson.SnakeIndented;

    public static DzlConfig Load(string path)
    {
        if (!File.Exists(path)) return DzlConfig.Default();
        var raw = File.ReadAllText(path);
        var saved = JsonSerializer.Deserialize<DzlConfig>(raw, Json) ?? DzlConfig.Default();
        return Migrate(raw, saved);
    }

    public static void Save(DzlConfig cfg, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Json));
    }

    // pre-per-mode params migration: old server_params/client_params -> *_debug.
    // (System.Text.Json ignores unknown keys, so read them from the raw doc.)
    private static DzlConfig Migrate(string raw, DzlConfig cfg)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return cfg;
        if (root.TryGetProperty("server_params", out var sp) && sp.ValueKind == JsonValueKind.Array
            && !root.TryGetProperty("server_params_debug", out _))
            cfg = cfg with { ServerParamsDebug = sp.EnumerateArray().Select(e => e.GetString()!).ToList() };
        if (root.TryGetProperty("client_params", out var cp) && cp.ValueKind == JsonValueKind.Array
            && !root.TryGetProperty("client_params_debug", out _))
            cfg = cfg with { ClientParamsDebug = cp.EnumerateArray().Select(e => e.GetString()!).ToList() };
        return cfg;
    }
}
