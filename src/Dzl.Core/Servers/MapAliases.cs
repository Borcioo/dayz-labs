namespace Dzl.Core.Servers;

public static class MapAliases
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chernarus"] = "dayzOffline.chernarusplus",
        ["livonia"]   = "dayzOffline.enoch",
        ["sakhal"]    = "dayzOffline.sakhal",
    };

    /// <summary>Map alias → mission template folder; if not a known alias, return the input unchanged
    /// (assumed to already be a template like "dayzOffline.namalsk").</summary>
    public static string MissionTemplate(string mapOrTemplate) =>
        Aliases.TryGetValue(mapOrTemplate.Trim(), out var t) ? t : mapOrTemplate.Trim();
}
