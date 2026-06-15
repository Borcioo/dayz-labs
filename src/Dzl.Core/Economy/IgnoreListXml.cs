using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfgignorelist.xml</c> — a flat list of item classnames the
/// Central Economy ignores (won't manage / cleanup). Structure: <c>&lt;ignore&gt;&lt;type name="X"/&gt;…&lt;/ignore&gt;</c>.
/// <see cref="Parse"/> never throws; edits mutate an <see cref="XDocument"/> preserving comments/order.
/// </summary>
public static class IgnoreListXml
{
    public static List<string> Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return new List<string>();
            return root.Elements("type")
                .Select(e => e.Attribute("name")?.Value?.Trim() ?? "")
                .Where(s => s.Length > 0)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    /// <summary>Append a <c>&lt;type name=…/&gt;</c>. Rejects a blank name or a case-insensitive duplicate.</summary>
    public static bool Add(XDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || doc.Root is not { } root) return false;
        if (root.Elements("type").Any(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase)))
            return false;
        root.Add(new XElement("type", new XAttribute("name", name)));
        return true;
    }

    /// <summary>Remove the <c>&lt;type&gt;</c> with this name. Returns true if one was removed.</summary>
    public static bool Remove(XDocument doc, string name)
    {
        var el = doc.Root?.Elements("type")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (el is null) return false;
        el.Remove();
        return true;
    }

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
