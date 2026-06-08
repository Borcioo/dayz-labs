using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One <c>&lt;child&gt;</c> row inside a CE <c>&lt;event&gt;</c>: classname + spawn-count + loot ranges.</summary>
public sealed record EventChild(string Type, int Min, int Max, int LootMin, int LootMax);

/// <summary>One <c>&lt;event name="…"&gt;</c> entry from <c>db/events.xml</c>.</summary>
public sealed record CeEvent(
    string Name,
    int Nominal,
    int Min,
    int Max,
    int Lifetime,
    int Restock,
    int SafeRadius,
    int DistanceRadius,
    int CleanupRadius,
    bool Deletable,
    bool InitRandom,
    bool RemoveDamaged,
    string Position,
    string Limit,
    bool Active,
    IReadOnlyList<EventChild> Children);

/// <summary>
/// Pure parse + in-place edit of a DayZ Central Economy <c>db/events.xml</c>. Each
/// <c>&lt;event name="…"&gt;</c> carries integer scalar children (nominal/min/max/lifetime/restock/
/// saferadius/distanceradius/cleanupradius), a <c>&lt;flags deletable init_random remove_damaged/&gt;</c>
/// element, <c>&lt;position&gt;</c>, <c>&lt;limit&gt;</c>, <c>&lt;active&gt;</c>, and zero or more
/// <c>&lt;child type min max lootmin lootmax/&gt;</c> rows.
/// <para>The read-only <see cref="Parse"/> never throws (returns an empty list on absent/malformed XML).
/// Booleans are encoded as "0"/"1". Missing scalars default to 0.</para>
/// <para>In-place edit methods mutate an <see cref="XDocument"/> obtained via <see cref="ParseDoc"/> and
/// preserve comments/order; serialize back with <see cref="ToXml"/>.</para>
/// </summary>
public static class EventsXml
{
    // ------------------------------------------------------------------
    // Small helpers
    // ------------------------------------------------------------------

    private static int ParseInt(string? raw) =>
        int.TryParse(raw?.Trim(), out var v) ? v : 0;

    private static bool ParseBool(string? raw) => raw?.Trim() == "1";

    private static string FormatBool(bool value) => value ? "1" : "0";

    private static string Txt(XElement parent, string child) =>
        parent.Element(child)?.Value?.Trim() ?? "";

    // ------------------------------------------------------------------
    // Read-only parse (never-throw)
    // ------------------------------------------------------------------

    /// <summary>Parse <c>db/events.xml</c> text into events (pure). Never throws — returns an empty list
    /// on absent or malformed XML.</summary>
    public static List<CeEvent> Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return new List<CeEvent>();

            var result = new List<CeEvent>();
            foreach (var el in root.Elements("event"))
            {
                var name = el.Attribute("name")?.Value?.Trim() ?? "";

                var flags = el.Element("flags");
                var deletable    = ParseBool(flags?.Attribute("deletable")?.Value);
                var initRandom   = ParseBool(flags?.Attribute("init_random")?.Value);
                var removeDamaged = ParseBool(flags?.Attribute("remove_damaged")?.Value);

                var children = el.Element("children")?.Elements("child")
                    .Select(c => new EventChild(
                        c.Attribute("type")?.Value?.Trim() ?? "",
                        ParseInt(c.Attribute("min")?.Value),
                        ParseInt(c.Attribute("max")?.Value),
                        ParseInt(c.Attribute("lootmin")?.Value),
                        ParseInt(c.Attribute("lootmax")?.Value)))
                    .Where(c => c.Type.Length > 0)
                    .ToList()
                    ?? new List<EventChild>();

                result.Add(new CeEvent(
                    name,
                    ParseInt(Txt(el, "nominal")),
                    ParseInt(Txt(el, "min")),
                    ParseInt(Txt(el, "max")),
                    ParseInt(Txt(el, "lifetime")),
                    ParseInt(Txt(el, "restock")),
                    ParseInt(Txt(el, "saferadius")),
                    ParseInt(Txt(el, "distanceradius")),
                    ParseInt(Txt(el, "cleanupradius")),
                    deletable,
                    initRandom,
                    removeDamaged,
                    Txt(el, "position"),
                    Txt(el, "limit"),
                    ParseBool(Txt(el, "active")),
                    children));
            }
            return result;
        }
        catch { return new List<CeEvent>(); }
    }

    // ------------------------------------------------------------------
    // In-place edit helpers
    // ------------------------------------------------------------------

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => XDocument.Parse(xml);

    /// <summary>Find an <c>&lt;event&gt;</c> element by name (case-insensitive), or null.</summary>
    private static XElement? FindEvent(XDocument doc, string name)
    {
        var root = doc.Root;
        if (root is null) return null;
        return root.Elements("event")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Add a new empty <c>&lt;event name="…"&gt;</c> with default structure. Returns true if added,
    /// false if the name already exists (case-insensitive) or is blank.</summary>
    public static bool AddEvent(XDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var root = doc.Root ?? throw new InvalidOperationException("events.xml has no root element");
        if (FindEvent(doc, name) is not null) return false;
        root.Add(new XElement("event",
            new XAttribute("name", name),
            new XElement("nominal", "0"),
            new XElement("min", "0"),
            new XElement("max", "10"),
            new XElement("lifetime", "0"),
            new XElement("restock", "0"),
            new XElement("saferadius", "0"),
            new XElement("distanceradius", "500"),
            new XElement("cleanupradius", "200"),
            new XElement("flags",
                new XAttribute("deletable", "0"),
                new XAttribute("init_random", "0"),
                new XAttribute("remove_damaged", "0")),
            new XElement("position", "random"),
            new XElement("limit", "mixed"),
            new XElement("active", "1"),
            new XElement("children")));
        return true;
    }

    /// <summary>Remove an event by name. Returns true if one was removed.</summary>
    public static bool RemoveEvent(XDocument doc, string name)
    {
        var el = FindEvent(doc, name);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Rename an event in place (preserves position/children). Returns true if performed; false if
    /// <paramref name="oldName"/> does not exist or <paramref name="newName"/> already exists.</summary>
    public static bool RenameEvent(XDocument doc, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var el = FindEvent(doc, oldName);
        if (el is null) return false;
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            FindEvent(doc, newName) is not null) return false;
        el.SetAttributeValue("name", newName);
        return true;
    }

    /// <summary>Set an integer scalar child element on an event (creates it if missing). Field names:
    /// nominal|min|max|lifetime|restock|saferadius|distanceradius|cleanupradius. Returns true if the event exists.</summary>
    public static bool SetScalar(XDocument doc, string eventName, string field, int value)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var el = ev.Element(field);
        if (el is null) { ev.Add(new XElement(field, value.ToString())); }
        else { el.Value = value.ToString(); }
        return true;
    }

    /// <summary>Set a flag attribute on the <c>&lt;flags&gt;</c> element. Flag names: deletable|init_random|remove_damaged.
    /// Returns true if the event exists (creates <c>&lt;flags&gt;</c> if missing).</summary>
    public static bool SetFlag(XDocument doc, string eventName, string flag, bool value)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var flags = ev.Element("flags");
        if (flags is null) { flags = new XElement("flags"); ev.Add(flags); }
        flags.SetAttributeValue(flag, FormatBool(value));
        return true;
    }

    /// <summary>Set the <c>&lt;position&gt;</c> text. Returns true if the event exists.</summary>
    public static bool SetPosition(XDocument doc, string eventName, string position)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var el = ev.Element("position");
        if (el is null) { ev.Add(new XElement("position", position)); }
        else { el.Value = position; }
        return true;
    }

    /// <summary>Set the <c>&lt;limit&gt;</c> text. Returns true if the event exists.</summary>
    public static bool SetLimit(XDocument doc, string eventName, string limit)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var el = ev.Element("limit");
        if (el is null) { ev.Add(new XElement("limit", limit)); }
        else { el.Value = limit; }
        return true;
    }

    /// <summary>Set the <c>&lt;active&gt;</c> value ("0"/"1"). Returns true if the event exists.</summary>
    public static bool SetActive(XDocument doc, string eventName, bool active)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var el = ev.Element("active");
        if (el is null) { ev.Add(new XElement("active", FormatBool(active))); }
        else { el.Value = FormatBool(active); }
        return true;
    }

    // ------------------------------------------------------------------
    // Child-level edits inside <children>
    // ------------------------------------------------------------------

    private static XElement? ChildrenOf(XElement ev)
    {
        var el = ev.Element("children");
        if (el is null) { el = new XElement("children"); ev.Add(el); }
        return el;
    }

    private static List<XElement> ChildElements(XElement ev) =>
        ev.Element("children")?.Elements("child").ToList() ?? new List<XElement>();

    private static XElement? FindChild(XElement ev, string type)
        => ChildElements(ev)
            .FirstOrDefault(c => string.Equals(c.Attribute("type")?.Value, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Add a <c>&lt;child&gt;</c> to an event. Returns true if added; false if the event is missing or
    /// a child of the same type already exists (case-insensitive).</summary>
    public static bool AddChild(XDocument doc, string eventName, EventChild child)
    {
        if (string.IsNullOrWhiteSpace(child.Type)) return false;
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        if (FindChild(ev, child.Type) is not null) return false;
        var container = ChildrenOf(ev)!;
        container.Add(new XElement("child",
            new XAttribute("lootmax", child.LootMax),
            new XAttribute("lootmin", child.LootMin),
            new XAttribute("max", child.Max),
            new XAttribute("min", child.Min),
            new XAttribute("type", child.Type)));
        return true;
    }

    /// <summary>Remove a child by type from an event. Returns true if one was removed.</summary>
    public static bool RemoveChild(XDocument doc, string eventName, string type)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var el = FindChild(ev, type);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Update a child entry in place: set all numeric fields and optionally rename its type.
    /// Matches by <paramref name="type"/>. Returns true if the child exists; false if event/child is absent
    /// or the rename would clash with another child in the same event (case-insensitive).</summary>
    public static bool SetChild(XDocument doc, string eventName, string type, EventChild updated)
    {
        var ev = FindEvent(doc, eventName);
        if (ev is null) return false;
        var el = FindChild(ev, type);
        if (el is null) return false;

        if (!string.IsNullOrWhiteSpace(updated.Type) &&
            !string.Equals(updated.Type, type, StringComparison.OrdinalIgnoreCase))
        {
            var clash = ChildElements(ev)
                .Any(c => c != el && string.Equals(c.Attribute("type")?.Value, updated.Type, StringComparison.OrdinalIgnoreCase));
            if (clash) return false;
            el.SetAttributeValue("type", updated.Type);
        }
        el.SetAttributeValue("lootmax", updated.LootMax);
        el.SetAttributeValue("lootmin", updated.LootMin);
        el.SetAttributeValue("max", updated.Max);
        el.SetAttributeValue("min", updated.Min);
        return true;
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) =>
        doc.Declaration is null
            ? doc.Root!.ToString()
            : doc.Declaration + Environment.NewLine + doc.Root;
}
