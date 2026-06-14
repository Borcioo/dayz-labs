using System.Globalization;
using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>
/// Shared parse/serialize + element helpers for the CE XML editors (<c>*Xml</c> classes).
/// Serialization preserves the XML declaration when present and avoids the leading bare
/// newline when it is absent; lookups match the <c>name</c>-style attribute case-insensitively.
/// </summary>
public static class CeXml
{
    /// <summary>Parse XML text to an editable document for use with the in-place edit methods.</summary>
    public static XDocument ParseDoc(string xml) => XDocument.Parse(xml);

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// document body when no declaration exists (avoids a leading bare newline). Emits every top-level node
    /// (root plus any comment / processing-instruction before or after it), not just the root, so a
    /// load → save round-trip never drops a pre/post-root comment.</summary>
    public static string Serialize(XDocument doc)
    {
        var body = string.Concat(doc.Nodes().Select(n => n.ToString()));
        return doc.Declaration is null
            ? body
            : doc.Declaration + Environment.NewLine + body;
    }

    /// <summary>First element whose <paramref name="attr"/> attribute equals <paramref name="name"/>
    /// (case-insensitive, ignoring surrounding whitespace on either side), or null. <paramref name="excluding"/>
    /// skips one element — used by rename/clash checks to ignore the element being renamed.
    /// <para>The stored value is trimmed before comparing because every CE <c>Parse</c> trims the name it
    /// surfaces; without this the read key (trimmed) and the write key (raw) disagree, so an edit addressed
    /// by a displayed name would silently miss a whitespace-padded element.</para></summary>
    public static XElement? ByName(this IEnumerable<XElement> els, string name, string attr = "name", XElement? excluding = null) =>
        els.FirstOrDefault(e => e != excluding &&
            string.Equals(e.Attribute(attr)?.Value?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Rename guard shared by the CE editors: returns false on blank new name, missing old
    /// element, or a case-insensitive clash with another element; otherwise sets the (trimmed) attribute.</summary>
    public static bool RenameByName(IEnumerable<XElement> els, string oldName, string newName, string attr = "name")
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var trimmedNew = newName.Trim();
        var list = els as IReadOnlyCollection<XElement> ?? els.ToList();
        var el = list.ByName(oldName, attr);
        if (el is null) return false;
        if (!string.Equals(oldName?.Trim(), trimmedNew, StringComparison.OrdinalIgnoreCase) &&
            list.ByName(trimmedNew, attr, excluding: el) is not null) return false;
        el.SetAttributeValue(attr, trimmedNew);
        return true;
    }

    /// <summary>Set a child element's text value, creating the element when missing.</summary>
    public static void SetChildValue(this XElement parent, XName child, string value)
    {
        var el = parent.Element(child);
        if (el is null) parent.Add(new XElement(child, value));
        else el.Value = value;
    }
}

/// <summary>Invariant-culture numeric/bool parse + format shared by the CE XML editors.
/// CE files encode booleans as "0"/"1"; numbers must never pick up the OS culture.</summary>
public static class CeNum
{
    public static int Int(string? raw, int fallback = 0) =>
        int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static double Dbl(string? raw, double fallback = 0.0) =>
        double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static double? DblOpt(string? raw) =>
        double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static bool Bool01(string? raw) => raw?.Trim() == "1";

    public static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);

    // Round to 10 dp before formatting so binary-float noise (e.g. 0.55 + 0.05 → 0.6000000000000001) never
    // reaches the file, while keeping every realistic CE precision (chances ≤3 dp, coords well under 10 dp).
    public static string Str(double v) => System.Math.Round(v, 10).ToString(CultureInfo.InvariantCulture);

    public static string Str(bool v) => v ? "1" : "0";
}
