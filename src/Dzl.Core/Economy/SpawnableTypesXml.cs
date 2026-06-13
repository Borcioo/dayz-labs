using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One cargo/attachments BLOCK on a <c>&lt;type&gt;</c> in <c>cfgspawnabletypes.xml</c>. A block is
/// EITHER preset-based (<c>&lt;cargo preset="mixArmy"/&gt;</c> — <see cref="Preset"/> non-null) OR chance-based
/// (<c>&lt;cargo chance="0.5"&gt;&lt;item name=".." chance=".."/&gt;&lt;/cargo&gt;</c> — <see cref="Chance"/> +
/// <see cref="Items"/>). <see cref="IsAttachments"/> distinguishes <c>&lt;attachments&gt;</c> from
/// <c>&lt;cargo&gt;</c>. <see cref="PresetItem"/> is reused from <see cref="RandomPresetsXml"/>.</summary>
public sealed record SpawnBlock(
    bool IsAttachments,
    string? Preset,
    double? Chance,
    IReadOnlyList<PresetItem> Items)
{
    /// <summary>True when this is a preset reference (vs. an inline chance block).</summary>
    public bool IsPreset => Preset is not null;
}

/// <summary>One <c>&lt;type name="…"&gt;</c> entry from <c>cfgspawnabletypes.xml</c>: an optional
/// <c>&lt;hoarder/&gt;</c> flag, optional <c>&lt;damage min max/&gt;</c>, and zero or more cargo + attachments
/// blocks (split into <see cref="Cargo"/> and <see cref="Attachments"/> by element name).</summary>
public sealed record SpawnableType(
    string Name,
    bool Hoarder,
    double? DamageMin,
    double? DamageMax,
    IReadOnlyList<SpawnBlock> Cargo,
    IReadOnlyList<SpawnBlock> Attachments);

/// <summary>
/// Pure parse + in-place edit of a DayZ Central Economy <c>cfgspawnabletypes.xml</c>: per-type
/// hoarder flag, damage range, and cargo/attachments blocks (preset-based or chance-based).
/// </summary>
/// <remarks>The read-only <see cref="Parse"/> never throws (empty list on absent/malformed XML);
/// doubles use the invariant culture. The edit methods mutate an <see cref="XDocument"/> from
/// <see cref="ParseDoc"/> and preserve comments/order; serialize back with <see cref="ToXml"/>.</remarks>
public static class SpawnableTypesXml
{
    private static string BlockElementName(bool isAttachments) => isAttachments ? "attachments" : "cargo";

    private static double ParseDouble(string? raw) => CeNum.Dbl(raw);

    private static double? ParseDoubleOpt(string? raw) => CeNum.DblOpt(raw);

    private static string FormatDouble(double v) => CeNum.Str(v);

    /// <summary>Parse <c>cfgspawnabletypes.xml</c> text into spawnable types (pure). Never throws — returns an
    /// empty list on absent or malformed XML.</summary>
    public static List<SpawnableType> Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return new List<SpawnableType>();

            var result = new List<SpawnableType>();
            foreach (var type in root.Elements("type"))
            {
                var name = type.Attribute("name")?.Value?.Trim() ?? "";
                var hoarder = type.Element("hoarder") is not null;

                double? dMin = null, dMax = null;
                var dmg = type.Element("damage");
                if (dmg is not null)
                {
                    dMin = ParseDoubleOpt(dmg.Attribute("min")?.Value);
                    dMax = ParseDoubleOpt(dmg.Attribute("max")?.Value);
                }

                var cargo = type.Elements("cargo").Select(e => ReadBlock(e, isAttachments: false)).ToList();
                var attachments = type.Elements("attachments").Select(e => ReadBlock(e, isAttachments: true)).ToList();

                result.Add(new SpawnableType(name, hoarder, dMin, dMax, cargo, attachments));
            }
            return result;
        }
        catch { return new List<SpawnableType>(); }
    }

    private static SpawnBlock ReadBlock(XElement el, bool isAttachments)
    {
        var preset = el.Attribute("preset")?.Value?.Trim();
        if (!string.IsNullOrEmpty(preset))
            return new SpawnBlock(isAttachments, preset, null, new List<PresetItem>());

        var chance = ParseDoubleOpt(el.Attribute("chance")?.Value);
        var items = el.Elements("item")
            .Select(i => new PresetItem(
                i.Attribute("name")?.Value?.Trim() ?? "",
                ParseDouble(i.Attribute("chance")?.Value)))
            .Where(i => i.Name.Length > 0)
            .ToList();
        return new SpawnBlock(isAttachments, null, chance, items);
    }

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindType(XDocument doc, string name) =>
        doc.Root?.Elements("type").ByName(name);

    /// <summary>Add a new empty <c>&lt;type name="…"/&gt;</c>. Returns true if added, false if a type of the
    /// same name already exists (case-insensitive) or the name is blank.</summary>
    public static bool AddType(XDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var root = doc.Root ?? throw new InvalidOperationException("cfgspawnabletypes.xml has no root element");
        if (FindType(doc, name) is not null) return false;
        root.Add(new XElement("type", new XAttribute("name", name)));
        return true;
    }

    /// <summary>Remove a <c>&lt;type&gt;</c> by name. Returns true if one was removed.</summary>
    public static bool RemoveType(XDocument doc, string name)
    {
        var el = FindType(doc, name);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Rename a type in place (preserves position/children). Returns true if performed; false if
    /// <paramref name="oldName"/> does not exist or <paramref name="newName"/> already exists.</summary>
    public static bool RenameType(XDocument doc, string oldName, string newName) =>
        CeXml.RenameByName(doc.Root?.Elements("type") ?? Enumerable.Empty<XElement>(), oldName, newName);

    /// <summary>Set/clear the <c>&lt;hoarder/&gt;</c> flag on a type. Returns true if the type exists.</summary>
    public static bool SetHoarder(XDocument doc, string name, bool on)
    {
        var type = FindType(doc, name);
        if (type is null) return false;
        var existing = type.Element("hoarder");
        if (on)
        {
            if (existing is null) type.Add(new XElement("hoarder"));
        }
        else
        {
            existing?.Remove();
        }
        return true;
    }

    /// <summary>Set the <c>&lt;damage min max/&gt;</c> element on a type. Passing both nulls clears the damage
    /// element entirely. Returns true if the type exists.</summary>
    public static bool SetDamage(XDocument doc, string name, double? min, double? max)
    {
        var type = FindType(doc, name);
        if (type is null) return false;
        var existing = type.Element("damage");
        if (min is null && max is null)
        {
            existing?.Remove();
            return true;
        }
        var dmg = existing ?? new XElement("damage");
        if (dmg.Parent is null) type.Add(dmg);
        dmg.SetAttributeValue("min", min is null ? null : FormatDouble(min.Value));
        dmg.SetAttributeValue("max", max is null ? null : FormatDouble(max.Value));
        return true;
    }

    // Blocks are addressed by (type, isAttachments, index) where index counts only blocks of
    // that element name within the type.
    private static List<XElement> BlocksOf(XElement type, bool isAttachments) =>
        type.Elements(BlockElementName(isAttachments)).ToList();

    private static XElement? BlockAt(XDocument doc, string typeName, bool isAttachments, int index)
    {
        var type = FindType(doc, typeName);
        if (type is null) return null;
        var blocks = BlocksOf(type, isAttachments);
        return index >= 0 && index < blocks.Count ? blocks[index] : null;
    }

    /// <summary>Append a block to a type. When <paramref name="preset"/> is non-empty the block is
    /// preset-based; otherwise it is a chance-based block with the given <paramref name="chance"/> (default 1.0
    /// when null) and no items. Returns the new block's index within its kind, or -1 if the type is missing.</summary>
    public static int AddBlock(XDocument doc, string typeName, bool isAttachments, string? preset, double? chance)
    {
        var type = FindType(doc, typeName);
        if (type is null) return -1;
        XElement block;
        if (!string.IsNullOrWhiteSpace(preset))
        {
            block = new XElement(BlockElementName(isAttachments), new XAttribute("preset", preset!.Trim()));
        }
        else
        {
            block = new XElement(BlockElementName(isAttachments),
                new XAttribute("chance", FormatDouble(chance ?? 1.0)));
        }
        type.Add(block);
        return BlocksOf(type, isAttachments).IndexOf(block);
    }

    /// <summary>Remove the block at <paramref name="index"/> (within its kind). Returns true if removed.</summary>
    public static bool RemoveBlock(XDocument doc, string typeName, bool isAttachments, int index)
    {
        var block = BlockAt(doc, typeName, isAttachments, index);
        if (block is null) return false;
        block.Remove();
        return true;
    }

    /// <summary>Convert a block to preset-based: set its <c>preset</c> attribute and strip any <c>chance</c>
    /// attribute + inline <c>&lt;item&gt;</c> children. Returns true if the block exists.</summary>
    public static bool SetBlockPreset(XDocument doc, string typeName, bool isAttachments, int index, string preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return false;
        var block = BlockAt(doc, typeName, isAttachments, index);
        if (block is null) return false;
        block.SetAttributeValue("chance", null);
        block.Elements("item").Remove();
        block.SetAttributeValue("preset", preset.Trim());
        return true;
    }

    /// <summary>Convert a block to chance-based: set its <c>chance</c> attribute and strip any <c>preset</c>
    /// attribute (inline items are preserved). Returns true if the block exists.</summary>
    public static bool SetBlockChance(XDocument doc, string typeName, bool isAttachments, int index, double chance)
    {
        var block = BlockAt(doc, typeName, isAttachments, index);
        if (block is null) return false;
        block.SetAttributeValue("preset", null);
        block.SetAttributeValue("chance", FormatDouble(chance));
        return true;
    }

    /// <summary>Add an <c>&lt;item name="…" chance="…"/&gt;</c> to a chance block. Strips any <c>preset</c>
    /// attribute on the block (an item-bearing block cannot be preset-based). Returns true if added; false if
    /// the block is missing, the item name is blank, or an item of the same name already exists.</summary>
    public static bool AddItem(XDocument doc, string typeName, bool isAttachments, int index,
                               string itemName, double chance)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return false;
        var block = BlockAt(doc, typeName, isAttachments, index);
        if (block is null) return false;
        if (block.Elements("item").ByName(itemName) is not null) return false;
        block.SetAttributeValue("preset", null);
        block.Add(new XElement("item",
            new XAttribute("name", itemName.Trim()),
            new XAttribute("chance", FormatDouble(chance))));
        return true;
    }

    /// <summary>Remove an item by name from a chance block. Returns true if one was removed.</summary>
    public static bool RemoveItem(XDocument doc, string typeName, bool isAttachments, int index, string itemName)
    {
        var block = BlockAt(doc, typeName, isAttachments, index);
        if (block is null) return false;
        var el = block.Elements("item").ByName(itemName);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Update an item in a chance block: set its chance and optionally rename it. Matches by
    /// <paramref name="itemName"/>. Returns true if the item exists; false if the block/item is absent or a
    /// rename would clash with another item in the same block (case-insensitive).</summary>
    public static bool SetItem(XDocument doc, string typeName, bool isAttachments, int index,
                               string itemName, double chance, string? newName = null)
    {
        var block = BlockAt(doc, typeName, isAttachments, index);
        if (block is null) return false;
        var el = block.Elements("item").ByName(itemName);
        if (el is null) return false;

        if (!string.IsNullOrWhiteSpace(newName) &&
            !string.Equals(newName, itemName, StringComparison.OrdinalIgnoreCase))
        {
            if (block.Elements("item").ByName(newName!, excluding: el) is not null) return false;
            el.SetAttributeValue("name", newName.Trim());
        }
        el.SetAttributeValue("chance", FormatDouble(chance));
        return true;
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
