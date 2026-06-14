using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>The Central Economy spawn flags on a <c>&lt;type&gt;</c> (all default 0).</summary>
public sealed record TypeFlags
{
    public bool CountInCargo { get; init; }
    public bool CountInHoarder { get; init; }
    public bool CountInMap { get; init; } = true;
    public bool CountInPlayer { get; init; }
    public bool Crafted { get; init; }
    public bool Deloot { get; init; }
}

/// <summary>One <c>&lt;type name="…"&gt;</c> entry from types.xml (immutable view for read/edit/display).</summary>
public sealed record TypeEntry
{
    public string Name { get; init; } = "";
    public int Nominal { get; init; }
    public int Min { get; init; }
    public int Lifetime { get; init; }
    public int Restock { get; init; }
    public int QuantMin { get; init; } = -1;
    public int QuantMax { get; init; } = -1;
    public int Cost { get; init; } = 100;
    public TypeFlags Flags { get; init; } = new();
    public string Category { get; init; } = "";
    public IReadOnlyList<string> Usage { get; init; } = new List<string>();
    public IReadOnlyList<string> Value { get; init; } = new List<string>();   // tiers
    public IReadOnlyList<string> Tag { get; init; } = new List<string>();
    public string SourceFile { get; init; } = "";   // absolute path this entry was read from / saves to
}

/// <summary>
/// Pure parse + in-place edit of a DayZ Central Economy <c>types.xml</c>. Editing mutates the live
/// <see cref="XDocument"/> (updating/creating just the touched elements) so untouched entries, comments
/// and formatting survive a round-trip. I/O (locate / backup / save) lives in <c>TypesService</c>.
/// </summary>
public static class TypesXml
{
    private static int IntVal(XElement type, string child, int fallback)
    {
        var e = type.Element(child);
        return e is null ? fallback : CeNum.Int(e.Value, fallback);
    }

    // Default is false for every flag EXCEPT count_in_map: DayZ treats a missing/absent count_in_map as 1
    // (the item still counts toward the map distribution), which TypeFlags.CountInMap=true encodes. A null
    // attribute (no <flags> element, or the flag omitted) must therefore fall back to dflt, not to false —
    // otherwise reading then re-saving a flags-less <type> would silently emit count_in_map="0".
    private static bool FlagVal(XElement? flags, string attr, bool dflt = false)
    {
        var v = flags?.Attribute(attr)?.Value;
        return v is null ? dflt : CeNum.Bool01(v);
    }

    private static List<string> NamedChildren(XElement type, string child) =>
        type.Elements(child).Select(e => e.Attribute("name")?.Value.Trim() ?? "").Where(s => s.Length > 0).ToList();

    /// <summary>Read one <c>&lt;type&gt;</c> element into a <see cref="TypeEntry"/>, optionally
    /// stamping <see cref="TypeEntry.SourceFile"/> at construction (cheaper than a post-hoc
    /// <c>with</c> clone per entry when reading 20k+ types).</summary>
    public static TypeEntry ReadType(XElement type, string sourceFile = "")
    {
        var flags = type.Element("flags");
        return new TypeEntry
        {
            Name = type.Attribute("name")?.Value.Trim() ?? "",
            SourceFile = sourceFile,
            Nominal = IntVal(type, "nominal", 0),
            Min = IntVal(type, "min", 0),
            Lifetime = IntVal(type, "lifetime", 0),
            Restock = IntVal(type, "restock", 0),
            QuantMin = IntVal(type, "quantmin", -1),
            QuantMax = IntVal(type, "quantmax", -1),
            Cost = IntVal(type, "cost", 100),
            Flags = new TypeFlags
            {
                CountInCargo = FlagVal(flags, "count_in_cargo"),
                CountInHoarder = FlagVal(flags, "count_in_hoarder"),
                CountInMap = FlagVal(flags, "count_in_map", dflt: true),
                CountInPlayer = FlagVal(flags, "count_in_player"),
                Crafted = FlagVal(flags, "crafted"),
                Deloot = FlagVal(flags, "deloot"),
            },
            Category = type.Element("category")?.Attribute("name")?.Value.Trim() ?? "",
            Usage = NamedChildren(type, "usage"),
            Value = NamedChildren(type, "value"),
            Tag = NamedChildren(type, "tag"),
        };
    }

    /// <summary>Parse types.xml text into entries (pure). Throws on malformed XML — callers catch.</summary>
    public static List<TypeEntry> Parse(string xml) => Parse(xml, "");

    /// <summary>Like <see cref="Parse(string)"/> but stamps every entry's
    /// <see cref="TypeEntry.SourceFile"/> during the read, avoiding a per-entry clone.</summary>
    public static List<TypeEntry> Parse(string xml, string sourceFile)
    {
        var doc = XDocument.Parse(xml);
        return (doc.Root?.Elements("type") ?? Enumerable.Empty<XElement>())
            .Select(t => ReadType(t, sourceFile)).ToList();
    }

    /// <summary>Parse to an editable document (for in-place edits then <see cref="ToXml"/>).</summary>
    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static void SetInt(XElement type, string child, int value) =>
        type.SetChildValue(child, CeNum.Str(value));

    private static void SetNamed(XElement type, string child, IReadOnlyList<string> names)
    {
        type.Elements(child).Remove();
        foreach (var n in names) type.Add(new XElement(child, new XAttribute("name", n)));
    }

    /// <summary>Insert or update a <c>&lt;type&gt;</c> in <paramref name="doc"/> in place, touching only that
    /// element. Returns true if an existing entry was updated, false if a new one was appended.</summary>
    public static bool Upsert(XDocument doc, TypeEntry entry)
    {
        var root = doc.Root ?? throw new InvalidOperationException("types.xml has no root <types>");
        var type = root.Elements("type").ByName(entry.Name);
        var existed = type != null;
        if (type == null)
        {
            type = new XElement("type", new XAttribute("name", entry.Name));
            root.Add(type);
        }

        SetInt(type, "nominal", entry.Nominal);
        SetInt(type, "lifetime", entry.Lifetime);
        SetInt(type, "restock", entry.Restock);
        SetInt(type, "min", entry.Min);
        SetInt(type, "quantmin", entry.QuantMin);
        SetInt(type, "quantmax", entry.QuantMax);
        SetInt(type, "cost", entry.Cost);

        var flags = type.Element("flags") ?? new XElement("flags");
        if (flags.Parent == null) type.Add(flags);
        void F(string a, bool v) => flags.SetAttributeValue(a, CeNum.Str(v));
        F("count_in_cargo", entry.Flags.CountInCargo);
        F("count_in_hoarder", entry.Flags.CountInHoarder);
        F("count_in_map", entry.Flags.CountInMap);
        F("count_in_player", entry.Flags.CountInPlayer);
        F("crafted", entry.Flags.Crafted);
        F("deloot", entry.Flags.Deloot);

        type.Elements("category").Remove();
        if (entry.Category.Length > 0) type.Add(new XElement("category", new XAttribute("name", entry.Category)));
        SetNamed(type, "usage", entry.Usage);
        SetNamed(type, "value", entry.Value);
        SetNamed(type, "tag", entry.Tag);

        return existed;
    }

    /// <summary>Remove a <c>&lt;type&gt;</c> by name. Returns true if one was removed.</summary>
    public static bool Remove(XDocument doc, string name)
    {
        var type = doc.Root?.Elements("type").ByName(name);
        if (type == null) return false;
        type.Remove();
        return true;
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
