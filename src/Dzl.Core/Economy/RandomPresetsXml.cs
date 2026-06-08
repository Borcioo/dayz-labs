using System.Globalization;
using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>Which kind of random preset an operation targets. The element name in
/// <c>cfgrandompresets.xml</c> is <c>cargo</c> or <c>attachments</c>.</summary>
public enum PresetKind { Cargo, Attachments }

/// <summary>One <c>&lt;item name="…" chance="…"/&gt;</c> inside a random preset.</summary>
public sealed record PresetItem(string Name, double Chance);

/// <summary>One <c>&lt;cargo|attachments chance="…" name="…"&gt;</c> block from
/// <c>cfgrandompresets.xml</c> with its child items.</summary>
public sealed record RandomPreset(PresetKind Kind, string Name, double Chance, IReadOnlyList<PresetItem> Items);

/// <summary>
/// Pure parse + in-place edit of a DayZ Central Economy <c>cfgrandompresets.xml</c>. A preset is a
/// <c>&lt;cargo&gt;</c> or <c>&lt;attachments&gt;</c> element carrying a <c>name</c> + <c>chance</c> and a
/// list of <c>&lt;item&gt;</c> children (each with <c>name</c> + <c>chance</c>). Presets are referenced by
/// name from <c>cfgspawnabletypes.xml</c> (<c>&lt;cargo preset="foodHermit"/&gt;</c>).
/// <para>The read-only <see cref="Parse"/> never throws (returns an empty list on absent/malformed XML),
/// consistent with <see cref="LimitsXml"/>. Doubles are parsed/written with the invariant culture.</para>
/// <para>In-place edit methods mutate an <see cref="XDocument"/> obtained via <see cref="ParseDoc"/> and
/// preserve comments/order; serialize back with <see cref="ToXml"/>.</para>
/// </summary>
public static class RandomPresetsXml
{
    // ------------------------------------------------------------------
    // Kind <-> element name
    // ------------------------------------------------------------------

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

    private static double ParseChance(string? raw) =>
        double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    private static string FormatChance(double chance) =>
        chance.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // Read-only parse (never-throw)
    // ------------------------------------------------------------------

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
            foreach (var el in root.Elements())
            {
                var kind = KindOf(el.Name.LocalName);
                if (kind is null) continue;
                var name = el.Attribute("name")?.Value?.Trim() ?? "";
                var chance = ParseChance(el.Attribute("chance")?.Value);
                var items = el.Elements("item")
                    .Select(i => new PresetItem(
                        i.Attribute("name")?.Value?.Trim() ?? "",
                        ParseChance(i.Attribute("chance")?.Value)))
                    .Where(i => i.Name.Length > 0)
                    .ToList();
                result.Add(new RandomPreset(kind.Value, name, chance, items));
            }
            return result;
        }
        catch { return new List<RandomPreset>(); }
    }

    // ------------------------------------------------------------------
    // In-place edit helpers
    // ------------------------------------------------------------------

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => XDocument.Parse(xml);

    /// <summary>Find a preset element by kind + name (case-insensitive), or null.</summary>
    private static XElement? FindPreset(XDocument doc, PresetKind kind, string name)
    {
        var root = doc.Root;
        if (root is null) return null;
        var elementName = ElementName(kind);
        return root.Elements(elementName)
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
    }

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

    /// <summary>Remove a preset by kind + name. Returns true if one was removed.</summary>
    public static bool RemovePreset(XDocument doc, PresetKind kind, string name)
    {
        var el = FindPreset(doc, kind, name);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Rename a preset in place (preserves position/items). Returns true if performed; false if
    /// <paramref name="oldName"/> does not exist or <paramref name="newName"/> already exists for the kind.</summary>
    public static bool RenamePreset(XDocument doc, PresetKind kind, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var el = FindPreset(doc, kind, oldName);
        if (el is null) return false;
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            FindPreset(doc, kind, newName) is not null) return false;
        el.SetAttributeValue("name", newName);
        return true;
    }

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
        var exists = preset.Elements("item")
            .Any(e => string.Equals(e.Attribute("name")?.Value, itemName, StringComparison.OrdinalIgnoreCase));
        if (exists) return false;
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
        var el = preset.Elements("item")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, itemName, StringComparison.OrdinalIgnoreCase));
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
        var el = preset.Elements("item")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, itemName, StringComparison.OrdinalIgnoreCase));
        if (el is null) return false;

        if (!string.IsNullOrWhiteSpace(newName) &&
            !string.Equals(newName, itemName, StringComparison.OrdinalIgnoreCase))
        {
            var clash = preset.Elements("item")
                .Any(e => e != el && string.Equals(e.Attribute("name")?.Value, newName, StringComparison.OrdinalIgnoreCase));
            if (clash) return false;
            el.SetAttributeValue("name", newName);
        }
        el.SetAttributeValue("chance", FormatChance(chance));
        return true;
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) =>
        doc.Declaration is null
            ? doc.Root!.ToString()
            : doc.Declaration + Environment.NewLine + doc.Root;
}
