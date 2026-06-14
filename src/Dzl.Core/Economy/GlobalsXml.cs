using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One <c>&lt;var name="…" type="…" value="…"/&gt;</c> from <c>db/globals.xml</c>.</summary>
public sealed record GlobalVar(string Name, int Type, string Value);

/// <summary>
/// Pure parse + in-place edit of a DayZ Central Economy <c>db/globals.xml</c> — a flat
/// <c>&lt;variables&gt;</c> root holding <c>&lt;var name type value/&gt;</c> elements.
/// </summary>
/// <remarks><c>type</c> is an int code (0 = integer value, 1 = float value), stored verbatim.
/// <see cref="Parse"/> never throws (empty list on absent/malformed XML). The edit methods mutate
/// an <see cref="XDocument"/> from <see cref="ParseDoc"/> and preserve comments/order; serialize
/// back with <see cref="ToXml"/>.</remarks>
public static class GlobalsXml
{
    /// <summary>Parse <c>globals.xml</c> text into a list of vars (pure). Never throws — returns an
    /// empty list on absent or malformed XML.</summary>
    public static List<GlobalVar> Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return new List<GlobalVar>();

            var result = new List<GlobalVar>();
            foreach (var el in root.Elements("var"))
            {
                var name = el.Attribute("name")?.Value?.Trim() ?? "";
                if (name.Length == 0) continue;
                var typeRaw = el.Attribute("type")?.Value?.Trim() ?? "0";
                int.TryParse(typeRaw, out var type);
                var value = el.Attribute("value")?.Value ?? "";
                result.Add(new GlobalVar(name, type, value));
            }
            return result;
        }
        catch { return new List<GlobalVar>(); }
    }

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static XElement? FindVar(XDocument doc, string name) =>
        doc.Root?.Elements("var").ByName(name);

    /// <summary>Upsert a var: add a new <c>&lt;var&gt;</c> if <paramref name="name"/> doesn't exist,
    /// or update its type + value in place when it does. Always returns true.</summary>
    public static bool SetVar(XDocument doc, string name, int type, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var root = doc.Root ?? throw new InvalidOperationException("globals.xml has no root element");
        var el = FindVar(doc, name);
        if (el is null)
        {
            root.Add(new XElement("var",
                new XAttribute("name", name),
                new XAttribute("type", type.ToString()),
                new XAttribute("value", value)));
        }
        else
        {
            // Value-only upsert: keep the stored name verbatim (FindVar already matched it
            // case-insensitively). Re-casing an engine-consumed identifier here is RenameVar's job.
            el.SetAttributeValue("type", type.ToString());
            el.SetAttributeValue("value", value);
        }
        return true;
    }

    /// <summary>Remove a var by name. Returns true if one was removed.</summary>
    public static bool RemoveVar(XDocument doc, string name)
    {
        var el = FindVar(doc, name);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Rename a var in place (preserves position/type/value). Returns true if performed; false
    /// when <paramref name="oldName"/> does not exist or <paramref name="newName"/> already exists
    /// (case-insensitive, unless old == new).</summary>
    public static bool RenameVar(XDocument doc, string oldName, string newName) =>
        CeXml.RenameByName(doc.Root?.Elements("var") ?? Enumerable.Empty<XElement>(), oldName, newName);

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present; returns only the
    /// root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
