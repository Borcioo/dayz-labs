using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One territory-level tuning knob (<c>&lt;item name val/&gt;</c>): count caps, spawn radii, etc.</summary>
public sealed record EnvItem(string Name, string Val);

/// <summary>One agent spawn inside a territory (read-only display: which animal/infected class + chance).</summary>
public sealed record EnvSpawn(string Agent, string ConfigName, string Chance);

/// <summary>One <c>&lt;territory&gt;</c> in cfgenvironment.xml — an animal/infected herd or ambient group.</summary>
public sealed record EnvTerritory(
    string Type, string Name, string Behavior, string UsableFile,
    IReadOnlyList<EnvItem> Items, IReadOnlyList<EnvSpawn> Spawns);

/// <summary>Parsed cfgenvironment.xml: the registered territory files + the territory definitions.</summary>
public sealed record EnvConfig(IReadOnlyList<string> Files, IReadOnlyList<EnvTerritory> Territories);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfgenvironment.xml</c> — animal/infected territories. The
/// per-territory count/radius <c>&lt;item&gt;</c> knobs are the hand-tunable part (edited here); the spawn lists
/// are read-only display and the per-zone geometry lives in the referenced <c>env/*_territories.xml</c> files
/// (opened externally). <see cref="Parse"/> never throws; edits preserve comments/order.
/// </summary>
public static class EnvironmentXml
{
    private static XElement? Territories(XDocument doc) => doc.Root?.Element("territories");

    public static EnvConfig Parse(string xml)
    {
        try
        {
            var terr = XDocument.Parse(xml).Root?.Element("territories");
            if (terr is null) return new EnvConfig(System.Array.Empty<string>(), System.Array.Empty<EnvTerritory>());

            var files = terr.Elements("file")
                .Select(f => f.Attribute("path")?.Value?.Trim() ?? "")
                .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var territories = terr.Elements("territory").Select(t => new EnvTerritory(
                t.Attribute("type")?.Value?.Trim() ?? "",
                t.Attribute("name")?.Value?.Trim() ?? "",
                t.Attribute("behavior")?.Value?.Trim() ?? "",
                t.Element("file")?.Attribute("usable")?.Value?.Trim() ?? "",
                t.Elements("item").Select(i => new EnvItem(i.Attribute("name")?.Value?.Trim() ?? "", i.Attribute("val")?.Value?.Trim() ?? ""))
                    .Where(i => i.Name.Length > 0).ToList(),
                t.Elements("agent").SelectMany(a => a.Elements("spawn").Select(s => new EnvSpawn(
                    a.Attribute("type")?.Value?.Trim() ?? "",
                    s.Attribute("configName")?.Value?.Trim() ?? "",
                    s.Attribute("chance")?.Value?.Trim() ?? ""))).ToList()))
                .Where(t => t.Name.Length > 0).ToList();

            return new EnvConfig(files, territories);
        }
        catch { return new EnvConfig(System.Array.Empty<string>(), System.Array.Empty<EnvTerritory>()); }
    }

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    /// <summary>Upsert a territory-level <c>&lt;item name val/&gt;</c> (a direct child of the territory, not an
    /// agent-nested one). Returns false if the territory is missing or the name/value is blank.</summary>
    public static bool SetItem(XDocument doc, string territory, string itemName, string val)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        var t = Territories(doc)?.Elements("territory")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, territory, StringComparison.OrdinalIgnoreCase));
        if (t is null) return false;
        var item = t.Elements("item")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, itemName, StringComparison.OrdinalIgnoreCase));
        if (item is null) t.Add(new XElement("item", new XAttribute("name", itemName), new XAttribute("val", val)));
        else item.SetAttributeValue("val", val);
        return true;
    }

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
