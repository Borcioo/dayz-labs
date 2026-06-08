using System.Xml.Linq;

namespace Dzl.Core.Economy;

public sealed record LimitsDef(
    IReadOnlySet<string> Usage, IReadOnlySet<string> Value,
    IReadOnlySet<string> Tag, IReadOnlySet<string> Category)
{
    public static LimitsDef Empty { get; } = new(
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
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
    // ------------------------------------------------------------------
    // Read-only parse (existing, never-throw)
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // In-place edit helpers
    // ------------------------------------------------------------------

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => XDocument.Parse(xml);

    /// <summary>Map a <see cref="LimitsKind"/> to the XML container element name and child element name.</summary>
    private static (string Container, string Child) KindNames(LimitsKind kind) => kind switch
    {
        LimitsKind.Usage    => ("usageflags", "usage"),
        LimitsKind.Value    => ("valueflags", "value"),
        LimitsKind.Tag      => ("tags",        "tag"),
        LimitsKind.Category => ("categories",  "category"),
        _                   => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Find the container element for the given <paramref name="kind"/> under the document root,
    /// or return null if it does not exist. Never mutates the document.</summary>
    private static XElement? FindContainer(XElement root, string containerName) =>
        root.Element(containerName);

    /// <summary>Find or create the container element for the given <paramref name="kind"/> under the document root.</summary>
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
        var exists = container.Elements(child)
            .Any(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (exists) return false;
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
        var el = container.Elements(child)
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
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
        var el = container.Elements(child)
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, oldName, StringComparison.OrdinalIgnoreCase));
        if (el is null) return false;
        // Check that the new name doesn't already exist (other than the element we're renaming).
        var clash = container.Elements(child)
            .Any(e => e != el && string.Equals(e.Attribute("name")?.Value, newName, StringComparison.OrdinalIgnoreCase));
        if (clash) return false;
        el.SetAttributeValue("name", newName);
        return true;
    }

    /// <summary>Serialize back to text. Preserves the XML declaration if one is present;
    /// returns only the root element text when no declaration exists (avoids a leading bare newline).</summary>
    public static string ToXml(XDocument doc) =>
        doc.Declaration is null
            ? doc.Root!.ToString()
            : doc.Declaration + Environment.NewLine + doc.Root;
}
