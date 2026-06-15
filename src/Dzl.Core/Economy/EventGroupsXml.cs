using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One object in an event group: a class + local offset (x/y/z), yaw (a), loot range and deloot flag.</summary>
public sealed record EventGroupChild(
    string Type, double X, double Y, double Z, double A, int LootMin, int LootMax, bool Deloot);

/// <summary>One <c>&lt;group name="…"&gt;</c> in cfgeventgroups.xml — a set of objects spawned together by an event.</summary>
public sealed record EventGroup(string Name, IReadOnlyList<EventGroupChild> Children);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfgeventgroups.xml</c> — named groups of objects an event spawns
/// as a unit (<c>&lt;eventgroupdef&gt;&lt;group name&gt;&lt;child type x y z a lootmin lootmax deloot/&gt;</c>).
/// <see cref="Parse"/> never throws; edits mutate an <see cref="XDocument"/> preserving comments/order.
/// </summary>
public static class EventGroupsXml
{
    private static double Dbl(XElement e, string a) => CeNum.Dbl(e.Attribute(a)?.Value);
    private static int Int(XElement e, string a) => CeNum.Int(e.Attribute(a)?.Value, 0);
    private static string Str(double v) => CeNum.Str(v);

    public static List<EventGroup> Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return new List<EventGroup>();
            return root.Elements("group")
                .Select(g => new EventGroup(
                    g.Attribute("name")?.Value?.Trim() ?? "",
                    g.Elements("child").Select(c => new EventGroupChild(
                        c.Attribute("type")?.Value?.Trim() ?? "",
                        Dbl(c, "x"), Dbl(c, "y"), Dbl(c, "z"), Dbl(c, "a"),
                        Int(c, "lootmin"), Int(c, "lootmax"), CeNum.Bool01(c.Attribute("deloot")?.Value ?? "0"))).ToList()))
                .Where(g => g.Name.Length > 0)
                .ToList();
        }
        catch { return new List<EventGroup>(); }
    }

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindGroup(XDocument doc, string name) =>
        doc.Root?.Elements("group").FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));

    public static bool AddGroup(XDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || doc.Root is not { } root) return false;
        if (FindGroup(doc, name) is not null) return false;
        root.Add(new XElement("group", new XAttribute("name", name)));
        return true;
    }

    public static bool RemoveGroup(XDocument doc, string name)
    {
        var g = FindGroup(doc, name);
        if (g is null) return false;
        g.Remove();
        return true;
    }

    public static bool AddChild(XDocument doc, string group, string type, double x, double y, double z, double a, int lootMin, int lootMax, bool deloot)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        var g = FindGroup(doc, group);
        if (g is null) return false;
        g.Add(Child(type, x, y, z, a, lootMin, lootMax, deloot));
        return true;
    }

    public static bool RemoveChild(XDocument doc, string group, int index)
    {
        var list = FindGroup(doc, group)?.Elements("child").ToList();
        if (list is null || index < 0 || index >= list.Count) return false;
        list[index].Remove();
        return true;
    }

    public static bool SetChild(XDocument doc, string group, int index, string type, double x, double y, double z, double a, int lootMin, int lootMax, bool deloot)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        var list = FindGroup(doc, group)?.Elements("child").ToList();
        if (list is null || index < 0 || index >= list.Count) return false;
        var c = list[index];
        c.SetAttributeValue("type", type);
        c.SetAttributeValue("x", Str(x)); c.SetAttributeValue("y", Str(y)); c.SetAttributeValue("z", Str(z)); c.SetAttributeValue("a", Str(a));
        c.SetAttributeValue("lootmin", lootMin); c.SetAttributeValue("lootmax", lootMax);
        c.SetAttributeValue("deloot", deloot ? "1" : "0");
        return true;
    }

    private static XElement Child(string type, double x, double y, double z, double a, int lootMin, int lootMax, bool deloot) =>
        new("child",
            new XAttribute("type", type),
            new XAttribute("deloot", deloot ? "1" : "0"),
            new XAttribute("lootmax", lootMax), new XAttribute("lootmin", lootMin),
            new XAttribute("x", Str(x)), new XAttribute("z", Str(z)), new XAttribute("a", Str(a)), new XAttribute("y", Str(y)));

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
