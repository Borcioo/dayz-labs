using System.Xml.Linq;

namespace Dzl.Core.Economy;

public sealed record LimitsDef(
    IReadOnlySet<string> Usage, IReadOnlySet<string> Value,
    IReadOnlySet<string> Tag, IReadOnlySet<string> Category)
{
    public static LimitsDef Empty { get; } = new(
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

    /// <summary>Fold named combos (cfglimitsdefinitionuser.xml) into the known usage/value sets. A combo name
    /// is a valid usage/value reference in types.xml — the engine expands it to its member flags — so it must
    /// count as "known" for validation, autocomplete and the "add to dictionary" prompt. Value combos extend
    /// <see cref="Value"/>, all others extend <see cref="Usage"/> (matching the engine). Case-insensitive.
    /// Returns <c>this</c> unchanged when there are no combos. Tag/Category are untouched (no combos exist for them).</summary>
    public LimitsDef WithCombos(IReadOnlyList<LimitsUserGroup> combos)
    {
        if (combos.Count == 0) return this;
        var usage = new HashSet<string>(Usage, StringComparer.OrdinalIgnoreCase);
        var value = new HashSet<string>(Value, StringComparer.OrdinalIgnoreCase);
        foreach (var g in combos)
            (g.Kind == LimitsKind.Value ? value : usage).Add(g.Name);
        return this with { Usage = usage, Value = value };
    }
}

/// <summary>Which name-list in <c>cfglimitsdefinition.xml</c> an operation targets.</summary>
public enum LimitsKind { Usage, Value, Tag, Category }

/// <summary>Parse a mission's <c>cfglimitsdefinition.xml</c> into the valid usage/value/tag/category names.
/// Never throws — returns <see cref="LimitsDef.Empty"/> when the file is absent or malformed.
/// Name comparisons are case-insensitive.
/// <para>In-place edit methods (<see cref="AddName"/>, <see cref="RemoveName"/>, <see cref="RenameName"/>)
/// mutate an <see cref="XDocument"/> obtained via <see cref="ParseDoc"/> and preserve comments/order.</para>
/// </summary>
public static class LimitsXml
{
    public static LimitsDef Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            HashSet<string> Names(string container, string item) =>
                doc.Descendants(container).Elements(item)
                   .Select(e => e.Attribute("name")?.Value?.Trim() ?? "")
                   .Where(s => s.Length > 0)
                   .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new LimitsDef(
                Names("usageflags", "usage"),
                Names("valueflags", "value"),
                Names("tags", "tag"),
                Names("categories", "category"));
        }
        catch { return LimitsDef.Empty; }
    }

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    private static (string Container, string Child) KindNames(LimitsKind kind) => kind switch
    {
        LimitsKind.Usage    => ("usageflags", "usage"),
        LimitsKind.Value    => ("valueflags", "value"),
        LimitsKind.Tag      => ("tags",        "tag"),
        LimitsKind.Category => ("categories",  "category"),
        _                   => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static XElement? FindContainer(XElement root, string containerName) =>
        root.Element(containerName);

    private static XElement GetOrCreateContainer(XDocument doc, LimitsKind kind)
    {
        var (containerName, _) = KindNames(kind);
        var root = doc.Root ?? throw new InvalidOperationException("cfglimitsdefinition.xml has no root element");
        var container = root.Element(containerName);
        if (container is null)
        {
            container = new XElement(containerName);
            root.Add(container);
        }
        return container;
    }

    /// <summary>Add <c>&lt;usage|value|tag|category name="…"/&gt;</c> under the appropriate container,
    /// creating the container if it is absent. Returns true if added, false if the name already exists
    /// (case-insensitive).</summary>
    public static bool AddName(XDocument doc, LimitsKind kind, string name)
    {
        var (_, child) = KindNames(kind);
        var container = GetOrCreateContainer(doc, kind);
        if (container.Elements(child).ByName(name) is not null) return false;
        container.Add(new XElement(child, new XAttribute("name", name)));
        return true;
    }

    /// <summary>Remove the named entry from the appropriate container. Returns true if an entry was removed.
    /// If the container does not exist the document is not mutated and false is returned.</summary>
    public static bool RemoveName(XDocument doc, LimitsKind kind, string name)
    {
        var (containerName, child) = KindNames(kind);
        var root = doc.Root;
        if (root is null) return false;
        var container = FindContainer(root, containerName);
        if (container is null) return false;
        var el = container.Elements(child).ByName(name);
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Rename an entry in place (preserves position). Returns true if the rename was performed;
    /// false if <paramref name="oldName"/> does not exist or <paramref name="newName"/> already exists.
    /// If the container does not exist the document is not mutated and false is returned.</summary>
    public static bool RenameName(XDocument doc, LimitsKind kind, string oldName, string newName)
    {
        var (containerName, child) = KindNames(kind);
        var root = doc.Root;
        if (root is null) return false;
        var container = FindContainer(root, containerName);
        if (container is null) return false;
        return CeXml.RenameByName(container.Elements(child), oldName, newName);
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present;
    /// returns only the root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
