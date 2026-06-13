using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>Which kind of random preset an operation targets. The element name in
/// <c>cfgrandompresets.xml</c> is <c>cargo</c> or <c>attachments</c>.</summary>
public enum PresetKind { Cargo, Attachments }

/// <summary>One <c>&lt;item name="…" chance="…"/&gt;</c> inside a random preset.</summary>
public sealed record PresetItem(string Name, double Chance);

/// <summary>One <c>&lt;cargo|attachments chance="…" name="…"&gt;</c> block from
/// <c>cfgrandompresets.xml</c> with its child items. <paramref name="Disabled"/> is true when the preset is
/// commented out in the file (kept for re-enabling but inert — the game never loads it).</summary>
public sealed record RandomPreset(PresetKind Kind, string Name, double Chance, IReadOnlyList<PresetItem> Items,
    bool Disabled = false);

/// <summary>
/// Pure parse + in-place edit of a DayZ Central Economy <c>cfgrandompresets.xml</c> — the named
/// <c>&lt;cargo&gt;</c>/<c>&lt;attachments&gt;</c> presets referenced from <c>cfgspawnabletypes.xml</c>.
/// </summary>
/// <remarks>The read-only <see cref="Parse"/> never throws (empty list on absent/malformed XML);
/// doubles use the invariant culture. The edit methods mutate an <see cref="XDocument"/> from
/// <see cref="ParseDoc"/> and preserve comments/order; serialize back with <see cref="ToXml"/>.</remarks>
public static class RandomPresetsXml
{
    private static string ElementName(PresetKind kind) => kind switch
    {
        PresetKind.Cargo       => "cargo",
        PresetKind.Attachments => "attachments",
        _                      => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static PresetKind? KindOf(string elementName) => elementName switch
    {
        "cargo"       => PresetKind.Cargo,
        "attachments" => PresetKind.Attachments,
        _             => null,
    };

    private static double ParseChance(string? raw) => CeNum.Dbl(raw);

    private static string FormatChance(double chance) => CeNum.Str(chance);

    /// <summary>Parse <c>cfgrandompresets.xml</c> text into presets (pure). Never throws — returns an
    /// empty list on absent or malformed XML.</summary>
    public static List<RandomPreset> Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return new List<RandomPreset>();

            var result = new List<RandomPreset>();
            foreach (var node in root.Nodes())
            {
                if (node is XElement el && KindOf(el.Name.LocalName) is { } kind)
                    result.Add(BuildPreset(kind, el, disabled: false));
                else if (node is XComment c && TryParseDisabled(c, out var dk, out _, out var dEl))
                    result.Add(BuildPreset(dk, dEl!, disabled: true));
            }
            return result;
        }
        catch { return new List<RandomPreset>(); }
    }

    private static RandomPreset BuildPreset(PresetKind kind, XElement el, bool disabled)
    {
        var name = el.Attribute("name")?.Value?.Trim() ?? "";
        var chance = ParseChance(el.Attribute("chance")?.Value);
        var items = el.Elements("item")
            .Select(i => new PresetItem(
                i.Attribute("name")?.Value?.Trim() ?? "",
                ParseChance(i.Attribute("chance")?.Value)))
            .Where(i => i.Name.Length > 0)
            .ToList();
        return new RandomPreset(kind, name, chance, items, disabled);
    }

    /// <summary>True when a comment node holds exactly one commented-out preset element (our disable format).
    /// Out-params expose its kind/name and the parsed element so callers can restore or remove it.</summary>
    private static bool TryParseDisabled(XComment c, out PresetKind kind, out string name, out XElement? element)
    {
        kind = default; name = ""; element = null;
        var inner = c.Value.Trim();
        if (inner.Length == 0 || inner[0] != '<') return false;
        try
        {
            var el = XElement.Parse(inner);
            if (KindOf(el.Name.LocalName) is not { } k) return false;
            kind = k;
            name = el.Attribute("name")?.Value?.Trim() ?? "";
            element = el;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindPreset(XDocument doc, PresetKind kind, string name) =>
        doc.Root?.Elements(ElementName(kind)).ByName(name);

    private static XComment? FindDisabled(XDocument doc, PresetKind kind, string name) =>
        doc.Root?.Nodes().OfType<XComment>().FirstOrDefault(c =>
            TryParseDisabled(c, out var k, out var n, out _) &&
            k == kind && string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Add a new <c>&lt;cargo|attachments name="…" chance="…"&gt;</c> preset (empty item list).
    /// Returns true if added, false if a preset of the same kind + name already exists (case-insensitive).</summary>
    public static bool AddPreset(XDocument doc, PresetKind kind, string name, double chance)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var root = doc.Root ?? throw new InvalidOperationException("cfgrandompresets.xml has no root element");
        if (FindPreset(doc, kind, name) is not null) return false;
        root.Add(new XElement(ElementName(kind),
            new XAttribute("chance", FormatChance(chance)),
            new XAttribute("name", name)));
        return true;
    }

    /// <summary>Remove a preset by kind + name — active or disabled (commented). Returns true if removed.</summary>
    public static bool RemovePreset(XDocument doc, PresetKind kind, string name)
    {
        if (FindPreset(doc, kind, name) is { } el) { el.Remove(); return true; }
        if (FindDisabled(doc, kind, name) is { } c) { c.Remove(); return true; }
        return false;
    }

    /// <summary>Disable a preset by commenting its element out in place (kept, not deleted, so it can be
    /// re-enabled later). No-op-false if the preset is absent, already disabled, or its serialized form
    /// contains <c>--</c> (illegal inside an XML comment). Returns true when it was commented out.</summary>
    public static bool DisablePreset(XDocument doc, PresetKind kind, string name)
    {
        var el = FindPreset(doc, kind, name);
        if (el is null) return false;
        var text = el.ToString(SaveOptions.DisableFormatting);
        if (text.Contains("--", StringComparison.Ordinal)) return false;
        el.ReplaceWith(new XComment($" {text} "));
        return true;
    }

    /// <summary>Re-enable a previously disabled (commented) preset of the given kind + name. Returns true
    /// when a matching disabled preset was found and restored to a live element.</summary>
    public static bool EnablePreset(XDocument doc, PresetKind kind, string name)
    {
        var c = FindDisabled(doc, kind, name);
        if (c is null || !TryParseDisabled(c, out _, out _, out var el)) return false;
        c.ReplaceWith(el!);
        return true;
    }

    /// <summary>Rename a preset in place (preserves position/items). Returns true if performed; false if
    /// <paramref name="oldName"/> does not exist or <paramref name="newName"/> already exists for the kind.</summary>
    public static bool RenamePreset(XDocument doc, PresetKind kind, string oldName, string newName) =>
        CeXml.RenameByName(doc.Root?.Elements(ElementName(kind)) ?? Enumerable.Empty<XElement>(), oldName, newName);

    /// <summary>Set a preset's <c>chance</c> attribute. Returns true if the preset exists.</summary>
    public static bool SetPresetChance(XDocument doc, PresetKind kind, string name, double chance)
    {
        var el = FindPreset(doc, kind, name);
        if (el is null) return false;
        el.SetAttributeValue("chance", FormatChance(chance));
        return true;
    }

    /// <summary>Add an <c>&lt;item name="…" chance="…"/&gt;</c> to a preset. Returns true if added; false if
    /// the preset does not exist or an item of the same name already exists in it (case-insensitive).</summary>
    public static bool AddItem(XDocument doc, PresetKind kind, string presetName, string itemName, double chance)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        var preset = FindPreset(doc, kind, presetName);
        if (preset is null) return false;
        if (preset.Elements("item").ByName(itemName) is not null) return false;
        preset.Add(new XElement("item",
            new XAttribute("name", itemName),
            new XAttribute("chance", FormatChance(chance))));
        return true;
    }

    /// <summary>Remove an item by name from a preset. Returns true if one was removed.</summary>
    public static bool RemoveItem(XDocument doc, PresetKind kind, string presetName, string itemName)
    {
        var preset = FindPreset(doc, kind, presetName);
        if (preset is null) return false;
        var el = preset.Elements("item").ByName(itemName);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Update an existing item in place: set its chance and optionally rename it. Matches by
    /// <paramref name="itemName"/>. Returns true if the item exists; false if the preset/item is absent or
    /// the rename would clash with another item in the same preset (case-insensitive).</summary>
    public static bool SetItem(XDocument doc, PresetKind kind, string presetName, string itemName,
                               double chance, string? newName = null)
    {
        var preset = FindPreset(doc, kind, presetName);
        if (preset is null) return false;
        var el = preset.Elements("item").ByName(itemName);
        if (el is null) return false;

        if (!string.IsNullOrWhiteSpace(newName) &&
            !string.Equals(newName, itemName, StringComparison.OrdinalIgnoreCase))
        {
            if (preset.Elements("item").ByName(newName!, excluding: el) is not null) return false;
            el.SetAttributeValue("name", newName);
        }
        el.SetAttributeValue("chance", FormatChance(chance));
        return true;
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
