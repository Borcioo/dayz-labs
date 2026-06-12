using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One scalar child element inside a param bag (e.g. <c>&lt;grid_width&gt;200&lt;/grid_width&gt;</c>):
/// its element name and raw text value. Values are kept as strings because the bag mixes int/float/bool-text
/// fields whose names vary per mission.</summary>
public sealed record SpawnParam(string Name, string Value);

/// <summary>One <c>&lt;pos x z/&gt;</c> spawn point inside a group.</summary>
public sealed record SpawnPos(double X, double Z);

/// <summary>One named <c>&lt;group&gt;</c> of <see cref="SpawnPos"/> points inside a bubbles container.</summary>
public sealed record SpawnGroup(string Name, IReadOnlyList<SpawnPos> Positions);

/// <summary>A bubbles container (<c>generator_posbubbles</c> or <c>permanent</c>) holding named groups.</summary>
public sealed record SpawnBubbles(string Container, IReadOnlyList<SpawnGroup> Groups);

/// <summary>One top-level spawn category (<c>fresh</c>/<c>hop</c>/<c>travel</c>) with its three optional param
/// bags (each a flat list of scalar children) and zero or more bubbles containers.</summary>
public sealed record SpawnCategory(
    string Name,
    IReadOnlyList<SpawnParam> SpawnParams,
    IReadOnlyList<SpawnParam> GeneratorParams,
    IReadOnlyList<SpawnParam> GroupParams,
    IReadOnlyList<SpawnBubbles> Bubbles);

/// <summary>
/// Pure parse + in-place edit of a DayZ mission <c>cfgplayerspawnpoints.xml</c>. The file is hierarchical:
/// <c>&lt;playerspawnpoints&gt;</c> → categories <c>&lt;fresh&gt;/&lt;hop&gt;/&lt;travel&gt;</c> (any may be
/// absent) → each category has optional <c>&lt;spawn_params&gt;</c>, <c>&lt;generator_params&gt;</c>,
/// <c>&lt;group_params&gt;</c> (each a flat bag of scalar child elements whose names vary) plus
/// <c>&lt;generator_posbubbles&gt;</c> / <c>&lt;permanent&gt;</c> containers of named <c>&lt;group&gt;</c>s,
/// each holding <c>&lt;pos x z/&gt;</c> points.
/// <para>The read-only <see cref="Parse"/> never throws (returns an empty list on absent/malformed XML).
/// Position doubles are parsed/written with the invariant culture, mirroring <see cref="RandomPresetsXml"/>.</para>
/// <para>In-place edit methods mutate an <see cref="XDocument"/> obtained via <see cref="ParseDoc"/> and
/// preserve comments/order; serialize back with <see cref="ToXml"/>. They operate on existing categories;
/// <see cref="SetParam"/> will create a missing param section under an existing category.</para>
/// </summary>
public static class PlayerSpawnsXml
{
    /// <summary>The three param-bag section names, in canonical order.</summary>
    public static readonly string[] Sections = { "spawn_params", "generator_params", "group_params" };

    private static double ParseDouble(string? raw) => CeNum.Dbl(raw);

    private static string FormatDouble(double value) => CeNum.Str(value);

    // ------------------------------------------------------------------
    // Read-only parse (never-throw)
    // ------------------------------------------------------------------

    /// <summary>Parse <c>cfgplayerspawnpoints.xml</c> text into categories (pure). Never throws — returns an
    /// empty list on absent or malformed XML.</summary>
    public static List<SpawnCategory> Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return new List<SpawnCategory>();

            var result = new List<SpawnCategory>();
            foreach (var cat in root.Elements())
            {
                var bubbles = new List<SpawnBubbles>();
                foreach (var container in cat.Elements())
                {
                    var local = container.Name.LocalName;
                    if (local is "spawn_params" or "generator_params" or "group_params") continue;
                    // Treat any element that contains <group> children as a bubbles container.
                    var groupEls = container.Elements("group").ToList();
                    if (groupEls.Count == 0) continue;
                    var groups = groupEls
                        .Select(g => new SpawnGroup(
                            g.Attribute("name")?.Value?.Trim() ?? "",
                            g.Elements("pos")
                                .Select(p => new SpawnPos(
                                    ParseDouble(p.Attribute("x")?.Value),
                                    ParseDouble(p.Attribute("z")?.Value)))
                                .ToList()))
                        .ToList();
                    bubbles.Add(new SpawnBubbles(local, groups));
                }

                result.Add(new SpawnCategory(
                    cat.Name.LocalName,
                    ReadParams(cat, "spawn_params"),
                    ReadParams(cat, "generator_params"),
                    ReadParams(cat, "group_params"),
                    bubbles));
            }
            return result;
        }
        catch { return new List<SpawnCategory>(); }
    }

    private static List<SpawnParam> ReadParams(XElement cat, string section)
    {
        var el = cat.Element(section);
        if (el is null) return new List<SpawnParam>();
        return el.Elements()
            .Select(c => new SpawnParam(c.Name.LocalName, (c.Value ?? "").Trim()))
            .ToList();
    }

    // ------------------------------------------------------------------
    // In-place edit helpers
    // ------------------------------------------------------------------

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindCategory(XDocument doc, string category)
    {
        var root = doc.Root;
        if (root is null) return null;
        return root.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, category, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement? FindContainer(XElement cat, string container) =>
        cat.Elements()
           .FirstOrDefault(e => string.Equals(e.Name.LocalName, container, StringComparison.OrdinalIgnoreCase));

    private static XElement? FindGroup(XElement container, string groupName) =>
        container.Elements("group").ByName(groupName);

    /// <summary>Upsert a scalar param element (<paramref name="name"/> = <paramref name="value"/>) inside the
    /// given section of a category. Creates the section if it is missing (under an existing category). Returns
    /// true if the category exists; false otherwise (does not create categories from scratch).</summary>
    public static bool SetParam(XDocument doc, string category, string section, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var cat = FindCategory(doc, category);
        if (cat is null) return false;

        var sec = cat.Element(section);
        if (sec is null) { sec = new XElement(section); cat.Add(sec); }

        var el = sec.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
        if (el is null) { sec.Add(new XElement(name, value)); }
        else { el.Value = value; }
        return true;
    }

    /// <summary>Add a new empty named <c>&lt;group&gt;</c> to a category's container. Creates the container if
    /// missing (under an existing category). Returns true if added; false if the category is missing, the name
    /// is blank, or a group of that name already exists (case-insensitive).</summary>
    public static bool AddGroup(XDocument doc, string category, string container, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return false;
        var cat = FindCategory(doc, category);
        if (cat is null) return false;

        var cont = FindContainer(cat, container);
        if (cont is null) { cont = new XElement(container); cat.Add(cont); }
        if (FindGroup(cont, groupName) is not null) return false;

        cont.Add(new XElement("group", new XAttribute("name", groupName)));
        return true;
    }

    /// <summary>Remove a named group from a category's container. Returns true if one was removed.</summary>
    public static bool RemoveGroup(XDocument doc, string category, string container, string groupName)
    {
        var cat = FindCategory(doc, category);
        if (cat is null) return false;
        var cont = FindContainer(cat, container);
        if (cont is null) return false;
        var g = FindGroup(cont, groupName);
        if (g is null) return false;
        g.Remove();
        return true;
    }

    /// <summary>Rename a group in place (preserves its positions). Returns true if performed; false if the
    /// group does not exist or the new name clashes with another group in the same container.</summary>
    public static bool RenameGroup(XDocument doc, string category, string container, string oldName, string newName)
    {
        var cat = FindCategory(doc, category);
        if (cat is null) return false;
        var cont = FindContainer(cat, container);
        if (cont is null) return false;
        return CeXml.RenameByName(cont.Elements("group"), oldName, newName);
    }

    /// <summary>Append a <c>&lt;pos x z/&gt;</c> to a group. Returns true if the group exists.</summary>
    public static bool AddPos(XDocument doc, string category, string container, string groupName, double x, double z)
    {
        var g = ResolveGroup(doc, category, container, groupName);
        if (g is null) return false;
        g.Add(new XElement("pos",
            new XAttribute("x", FormatDouble(x)),
            new XAttribute("z", FormatDouble(z))));
        return true;
    }

    /// <summary>Remove the position at <paramref name="index"/> from a group. Returns true if removed; false on
    /// missing group or out-of-range index.</summary>
    public static bool RemovePos(XDocument doc, string category, string container, string groupName, int index)
    {
        var g = ResolveGroup(doc, category, container, groupName);
        if (g is null) return false;
        var positions = g.Elements("pos").ToList();
        if (index < 0 || index >= positions.Count) return false;
        positions[index].Remove();
        return true;
    }

    /// <summary>Set the x/z of the position at <paramref name="index"/>. Returns true if set; false on missing
    /// group or out-of-range index.</summary>
    public static bool SetPos(XDocument doc, string category, string container, string groupName, int index, double x, double z)
    {
        var g = ResolveGroup(doc, category, container, groupName);
        if (g is null) return false;
        var positions = g.Elements("pos").ToList();
        if (index < 0 || index >= positions.Count) return false;
        positions[index].SetAttributeValue("x", FormatDouble(x));
        positions[index].SetAttributeValue("z", FormatDouble(z));
        return true;
    }

    private static XElement? ResolveGroup(XDocument doc, string category, string container, string groupName)
    {
        var cat = FindCategory(doc, category);
        if (cat is null) return null;
        var cont = FindContainer(cat, container);
        if (cont is null) return null;
        return FindGroup(cont, groupName);
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
