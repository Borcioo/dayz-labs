using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One <c>&lt;pos x z a/&gt;</c> spawn point of a dynamic event (a = yaw angle).</summary>
public sealed record EventPos(double X, double Z, double A);

/// <summary>One <c>&lt;event name="…"&gt;</c> in cfgeventspawns.xml with its spawn positions.</summary>
public sealed record EventSpawn(string Name, IReadOnlyList<EventPos> Positions);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfgeventspawns.xml</c> — per dynamic-event spawn positions
/// (<c>&lt;eventposdef&gt;&lt;event name&gt;&lt;pos x z a/&gt;</c>). <see cref="Parse"/> never throws; edits
/// mutate an <see cref="XDocument"/> preserving comments/order and touch only x/z/a (other pos attributes,
/// e.g. y/group/zone params, survive a round-trip).
/// </summary>
public static class EventSpawnsXml
{
    private static double Dbl(string? raw) => CeNum.Dbl(raw);
    private static string Str(double v) => CeNum.Str(v);

    public static List<EventSpawn> Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return new List<EventSpawn>();
            return root.Elements("event")
                .Select(e => new EventSpawn(
                    e.Attribute("name")?.Value?.Trim() ?? "",
                    e.Elements("pos").Select(p => new EventPos(
                        Dbl(p.Attribute("x")?.Value), Dbl(p.Attribute("z")?.Value), Dbl(p.Attribute("a")?.Value))).ToList()))
                .Where(ev => ev.Name.Length > 0)
                .ToList();
        }
        catch { return new List<EventSpawn>(); }
    }

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindEvent(XDocument doc, string name) =>
        doc.Root?.Elements("event").FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));

    public static bool AddEvent(XDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || doc.Root is not { } root) return false;
        if (FindEvent(doc, name) is not null) return false;
        root.Add(new XElement("event", new XAttribute("name", name)));
        return true;
    }

    public static bool RemoveEvent(XDocument doc, string name)
    {
        var e = FindEvent(doc, name);
        if (e is null) return false;
        e.Remove();
        return true;
    }

    public static bool AddPos(XDocument doc, string eventName, double x, double z, double a)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        ev.Add(new XElement("pos", new XAttribute("x", Str(x)), new XAttribute("z", Str(z)), new XAttribute("a", Str(a))));
        return true;
    }

    public static bool RemovePos(XDocument doc, string eventName, int index)
    {
        var ev = FindEvent(doc, eventName);
        var list = ev?.Elements("pos").ToList();
        if (list is null || index < 0 || index >= list.Count) return false;
        list[index].Remove();
        return true;
    }

    public static bool SetPos(XDocument doc, string eventName, int index, double x, double z, double a)
    {
        var ev = FindEvent(doc, eventName);
        var list = ev?.Elements("pos").ToList();
        if (list is null || index < 0 || index >= list.Count) return false;
        list[index].SetAttributeValue("x", Str(x));
        list[index].SetAttributeValue("z", Str(z));
        list[index].SetAttributeValue("a", Str(a));
        return true;
    }

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
