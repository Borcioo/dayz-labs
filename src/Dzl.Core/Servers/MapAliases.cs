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

    /// <summary>Mission template folder → friendly map alias (reverse of <see cref="MissionTemplate"/>);
    /// if the template isn't a known one, return it unchanged so callers can still display it.</summary>
    public static string MapName(string template)
    {
        var t = template.Trim();
        foreach (var kv in Aliases)
            if (string.Equals(kv.Value, t, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return t;
    }
}
