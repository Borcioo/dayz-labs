using System.Xml.Linq;

namespace Dzl.Core.Economy;

public sealed record LimitsDef(
    IReadOnlySet<string> Usage, IReadOnlySet<string> Value,
    IReadOnlySet<string> Tag, IReadOnlySet<string> Category)
{
    public static LimitsDef Empty { get; } = new(
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
}

/// <summary>Parse a mission's <c>cfglimitsdefinition.xml</c> into the valid usage/value/tag/category names.
/// Never throws — returns <see cref="LimitsDef.Empty"/> when the file is absent or malformed.
/// Name comparisons are case-insensitive.</summary>
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
}
