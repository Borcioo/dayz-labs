using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One CE root class (<c>&lt;rootclass name act reportMemoryLOD/&gt;</c>) — advanced, shown read-only.</summary>
public sealed record CeRootClass(string Name, string Act, bool ReportMemoryLod)
{
    /// <summary>True when an <c>act</c> (character/car) is set — drives the read-only badge.</summary>
    public bool HasAct => !string.IsNullOrEmpty(Act);
}

/// <summary>One <c>&lt;default name value/&gt;</c> tuning knob in cfgeconomycore.</summary>
public sealed record CeDefault(string Name, string Value);

/// <summary>One custom CE file registration: a <c>&lt;file name type/&gt;</c> under a <c>&lt;ce folder&gt;</c>.</summary>
public sealed record CeRoutedFile(string Folder, string Name, string Type);

/// <summary>Parsed view of cfgeconomycore.xml: root classes, default knobs, and the custom-file routing manifest.</summary>
public sealed record CeCoreConfig(
    IReadOnlyList<CeRootClass> RootClasses,
    IReadOnlyList<CeDefault> Defaults,
    IReadOnlyList<CeRoutedFile> Files);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfgeconomycore.xml</c> — the CE master config: root classes,
/// <c>&lt;defaults&gt;</c> tuning knobs (dynamic infected zones, CE logging, startup/persistence), and the
/// <c>&lt;ce folder&gt;&lt;file name type/&gt;</c> ROUTING manifest that registers custom CE files (the files
/// the other editor tabs add won't load until they're registered here). Never throws on parse; edits mutate an
/// <see cref="XDocument"/> preserving comments/order.
/// </summary>
public static class CeCoreXml
{
    /// <summary>The CE file types a routed file may declare (<c>type</c> attribute).</summary>
    public static readonly string[] FileTypes =
        { "types", "spawnabletypes", "globals", "economy", "events", "messages" };

    public static CeCoreConfig Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return Empty;

            var classes = (root.Element("classes")?.Elements("rootclass") ?? Enumerable.Empty<XElement>())
                .Select(e => new CeRootClass(
                    e.Attribute("name")?.Value?.Trim() ?? "",
                    e.Attribute("act")?.Value?.Trim() ?? "",
                    !string.Equals(e.Attribute("reportMemoryLOD")?.Value?.Trim(), "no", StringComparison.OrdinalIgnoreCase)))
                .Where(c => c.Name.Length > 0).ToList();

            var defaults = (root.Element("defaults")?.Elements("default") ?? Enumerable.Empty<XElement>())
                .Select(e => new CeDefault(e.Attribute("name")?.Value?.Trim() ?? "", e.Attribute("value")?.Value?.Trim() ?? ""))
                .Where(d => d.Name.Length > 0).ToList();

            var files = root.Elements("ce")
                .SelectMany(ce =>
                {
                    var folder = ce.Attribute("folder")?.Value?.Trim() ?? "";
                    return ce.Elements("file").Select(f => new CeRoutedFile(
                        folder, f.Attribute("name")?.Value?.Trim() ?? "", f.Attribute("type")?.Value?.Trim() ?? ""));
                })
                .Where(f => f.Name.Length > 0).ToList();

            return new CeCoreConfig(classes, defaults, files);
        }
        catch { return Empty; }
    }

    private static CeCoreConfig Empty =>
        new(Array.Empty<CeRootClass>(), Array.Empty<CeDefault>(), Array.Empty<CeRoutedFile>());

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    /// <summary>Upsert a <c>&lt;default name=… value=…/&gt;</c> (creates the <c>&lt;defaults&gt;</c> section if absent).</summary>
    public static bool SetDefault(XDocument doc, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || doc.Root is not { } root) return false;
        var sec = root.Element("defaults");
        if (sec is null) { sec = new XElement("defaults"); root.Add(sec); }
        var el = sec.Elements("default")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (el is null) sec.Add(new XElement("default", new XAttribute("name", name), new XAttribute("value", value)));
        else el.SetAttributeValue("value", value);
        return true;
    }

    /// <summary>Register a custom CE file under <c>&lt;ce folder&gt;</c> (creates the ce block if absent).
    /// Rejects an invalid type or a duplicate folder+name.</summary>
    public static bool AddFile(XDocument doc, string folder, string name, string type)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name) || doc.Root is not { } root) return false;
        if (Array.IndexOf(FileTypes, type) < 0) return false;
        var ce = FindCe(root, folder);
        if (ce is null) { ce = new XElement("ce", new XAttribute("folder", folder)); root.Add(ce); }
        if (ce.Elements("file").Any(f => string.Equals(f.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase)))
            return false;
        ce.Add(new XElement("file", new XAttribute("name", name), new XAttribute("type", type)));
        return true;
    }

    /// <summary>Remove a registered file; drops its (now empty) <c>&lt;ce&gt;</c> block too.</summary>
    public static bool RemoveFile(XDocument doc, string folder, string name)
    {
        if (doc.Root is not { } root) return false;
        var ce = FindCe(root, folder);
        var file = ce?.Elements("file")
            .FirstOrDefault(f => string.Equals(f.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (file is null) return false;
        file.Remove();
        if (!ce!.Elements("file").Any()) ce.Remove();
        return true;
    }

    private static XElement? FindCe(XElement root, string folder) =>
        root.Elements("ce").FirstOrDefault(e => string.Equals(e.Attribute("folder")?.Value, folder, StringComparison.OrdinalIgnoreCase));

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
