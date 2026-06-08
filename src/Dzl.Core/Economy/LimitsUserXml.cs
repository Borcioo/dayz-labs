using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>A named combination group from <c>cfglimitsdefinitionuser.xml</c>.</summary>
public sealed record LimitsUserGroup(string Name, LimitsKind Kind, IReadOnlyList<string> Members);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfglimitsdefinitionuser.xml</c>.
/// The file defines named combinations (user groups) under <c>&lt;user_lists&gt;</c>, for example:
/// <code>
/// &lt;user_lists&gt;
///   &lt;usageflags&gt;
///     &lt;user name="TownVillage"&gt;&lt;usage name="Town"/&gt;&lt;usage name="Village"/&gt;&lt;/user&gt;
///   &lt;/usageflags&gt;
///   &lt;valueflags&gt;
///     &lt;user name="Tier123"&gt;&lt;value name="Tier1"/&gt;&lt;/user&gt;
///   &lt;/valueflags&gt;
/// &lt;/user_lists&gt;
/// </code>
/// Only <c>usageflags</c> and <c>valueflags</c> sections are supported (the game only defines those two).
/// <para>Never throws — returns an empty list on absent or malformed XML.</para>
/// </summary>
public static class LimitsUserXml
{
    // ------------------------------------------------------------------
    // Internal helpers
    // ------------------------------------------------------------------

    private static (string Container, string MemberElem, LimitsKind Kind)[] Sections =>
    [
        ("usageflags", "usage", LimitsKind.Usage),
        ("valueflags", "value", LimitsKind.Value),
    ];

    private static (string Container, string MemberElem, LimitsKind Kind) SectionFor(LimitsKind kind) =>
        kind == LimitsKind.Usage ? ("usageflags", "usage", LimitsKind.Usage)
        : kind == LimitsKind.Value ? ("valueflags", "value", LimitsKind.Value)
        : throw new ArgumentOutOfRangeException(nameof(kind), "Only Usage and Value kinds are supported in user lists");

    // ------------------------------------------------------------------
    // Read-only parse — never throws, returns empty on malformed
    // ------------------------------------------------------------------

    /// <summary>Parse the user-lists file into groups. Returns an empty list when the XML is absent,
    /// empty, or malformed.</summary>
    public static List<LimitsUserGroup> Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            return ParseDoc(doc);
        }
        catch { return new List<LimitsUserGroup>(); }
    }

    private static List<LimitsUserGroup> ParseDoc(XDocument doc)
    {
        var result = new List<LimitsUserGroup>();
        var root = doc.Root;
        if (root is null) return result;

        // Support both <user_lists> as root and as a child element.
        var userLists = root.Name.LocalName == "user_lists" ? root : root.Element("user_lists");
        if (userLists is null) return result;

        foreach (var (containerName, memberElem, kind) in Sections)
        {
            var section = userLists.Element(containerName);
            if (section is null) continue;
            foreach (var user in section.Elements("user"))
            {
                var name = user.Attribute("name")?.Value?.Trim() ?? "";
                if (name.Length == 0) continue;
                var members = user.Elements(memberElem)
                    .Select(e => e.Attribute("name")?.Value?.Trim() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
                result.Add(new LimitsUserGroup(name, kind, members));
            }
        }
        return result;
    }

    // ------------------------------------------------------------------
    // In-place edit
    // ------------------------------------------------------------------

    /// <summary>Parse XML to an editable document for use with the edit methods.</summary>
    public static XDocument ParseDoc(string xml) => XDocument.Parse(xml);

    /// <summary>Ensure the document has a root <c>&lt;user_lists&gt;</c> element (creates one if absent)
    /// and return it.</summary>
    private static XElement GetOrCreateUserLists(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
        {
            root = new XElement("user_lists");
            doc.Add(root);
            return root;
        }
        if (root.Name.LocalName == "user_lists") return root;
        var ul = root.Element("user_lists");
        if (ul is not null) return ul;
        ul = new XElement("user_lists");
        root.Add(ul);
        return ul;
    }

    /// <summary>Get or create the section element (<c>usageflags</c> or <c>valueflags</c>) under
    /// <c>&lt;user_lists&gt;</c>.</summary>
    private static XElement GetOrCreateSection(XDocument doc, LimitsKind kind)
    {
        var (containerName, _, _) = SectionFor(kind);
        var userLists = GetOrCreateUserLists(doc);
        var section = userLists.Element(containerName);
        if (section is null)
        {
            section = new XElement(containerName);
            userLists.Add(section);
        }
        return section;
    }

    /// <summary>Add a new user group. If a group with <paramref name="name"/> already exists in the same
    /// <paramref name="kind"/> section it is replaced. No-op if the document is malformed.</summary>
    public static void AddGroup(XDocument doc, LimitsKind kind, string name, IReadOnlyList<string> members)
    {
        var (_, memberElem, _) = SectionFor(kind);
        var section = GetOrCreateSection(doc, kind);

        // Remove existing group with same name (case-insensitive).
        var existing = section.Elements("user")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
        existing?.Remove();

        var user = new XElement("user", new XAttribute("name", name));
        foreach (var m in members)
            user.Add(new XElement(memberElem, new XAttribute("name", m)));
        section.Add(user);
    }

    /// <summary>Remove the named group from the given <paramref name="kind"/> section.
    /// Returns true if a group was removed.</summary>
    public static bool RemoveGroup(XDocument doc, LimitsKind kind, string name)
    {
        var (containerName, _, _) = SectionFor(kind);
        var userLists = GetOrCreateUserLists(doc);
        var section = userLists.Element(containerName);
        if (section is null) return false;
        var el = section.Elements("user")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (el is null) return false;
        el.Remove();
        return true;
    }

    /// <summary>Replace the member list of an existing group in place (preserves position / comments).
    /// If the group does not exist it is created. Returns true if the group already existed (update),
    /// false if it was newly created.</summary>
    public static bool SetGroupMembers(XDocument doc, LimitsKind kind, string name, IReadOnlyList<string> members)
    {
        var (containerName, memberElem, _) = SectionFor(kind);
        var userLists = GetOrCreateUserLists(doc);
        var section = userLists.Element(containerName);
        XElement? user = section?.Elements("user")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            // Group doesn't exist — create it.
            AddGroup(doc, kind, name, members);
            return false;
        }

        // Replace members in-place.
        user.Elements(memberElem).Remove();
        foreach (var m in members)
            user.Add(new XElement(memberElem, new XAttribute("name", m)));
        return true;
    }

    /// <summary>Serialize back to text with the XML declaration preserved.</summary>
    public static string ToXml(XDocument doc) => doc.Declaration + Environment.NewLine + doc.Root;
}
