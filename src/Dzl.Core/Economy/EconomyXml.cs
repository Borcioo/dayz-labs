using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One <c>db/economy.xml</c> entity-group row: which lifecycle phases the Central Economy runs for
/// this group. Flags map to the element's <c>init</c>/<c>load</c>/<c>respawn</c>/<c>save</c> attributes (0/1).</summary>
public sealed record EconomyGroup(string Name, bool Init, bool Load, bool Respawn, bool Save);

/// <summary>
/// Pure parse + in-place edit of a DayZ mission <c>db/economy.xml</c> — the master switch board that toggles,
/// per entity group (<c>dynamic</c>/<c>animals</c>/<c>zombies</c>/<c>vehicles</c>/<c>randoms</c>/<c>custom</c>/
/// <c>building</c>/<c>player</c>), whether the CE initializes, loads from persistence, respawns at runtime, and
/// saves. <see cref="Parse"/> never throws; edit methods mutate an <see cref="XDocument"/> preserving order/comments.
/// </summary>
public static class EconomyXml
{
    /// <summary>The four per-group lifecycle flags, in canonical attribute order.</summary>
    public static readonly string[] Flags = { "init", "load", "respawn", "save" };

    public static List<EconomyGroup> Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return new List<EconomyGroup>();
            return root.Elements()
                .Select(e => new EconomyGroup(
                    e.Name.LocalName, Flag(e, "init"), Flag(e, "load"), Flag(e, "respawn"), Flag(e, "save")))
                .ToList();
        }
        catch { return new List<EconomyGroup>(); }
    }

    private static bool Flag(XElement e, string attr) => CeNum.Bool01(e.Attribute(attr)?.Value ?? "0");

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindGroup(XDocument doc, string group) =>
        doc.Root?.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, group, StringComparison.OrdinalIgnoreCase));

    /// <summary>Set one lifecycle flag on an existing group. Returns false if the flag name is invalid or the
    /// group is absent (use <see cref="SetGroup"/> to create one).</summary>
    public static bool SetFlag(XDocument doc, string group, string flag, bool value)
    {
        if (Array.IndexOf(Flags, flag) < 0) return false;
        var el = FindGroup(doc, group);
        if (el is null) return false;
        el.SetAttributeValue(flag, value ? "1" : "0");
        return true;
    }

    /// <summary>Upsert a group with all four flags (creates the element under an existing root if missing).
    /// Used to add a standard group or reset one to its defaults. Returns false only on a blank name / missing root.</summary>
    public static bool SetGroup(XDocument doc, string name, bool init, bool load, bool respawn, bool save)
    {
        if (string.IsNullOrWhiteSpace(name) || doc.Root is not { } root) return false;
        var el = FindGroup(doc, name);
        if (el is null) { el = new XElement(name); root.Add(el); }
        el.SetAttributeValue("init", init ? "1" : "0");
        el.SetAttributeValue("load", load ? "1" : "0");
        el.SetAttributeValue("respawn", respawn ? "1" : "0");
        el.SetAttributeValue("save", save ? "1" : "0");
        return true;
    }

    /// <summary>Remove a group element. Returns true if one was removed.</summary>
    public static bool RemoveGroup(XDocument doc, string name)
    {
        var el = FindGroup(doc, name);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
