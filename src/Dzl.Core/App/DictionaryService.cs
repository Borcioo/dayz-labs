using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active mission's CE dictionary files: <c>cfglimitsdefinition.xml</c>
/// (base name lists) and <c>cfglimitsdefinitionuser.xml</c> (named combinations of those names).
/// Never throws (returns ok+message); snapshots a backup before every write.
/// </summary>
public sealed class DictionaryService
{
    private readonly string _configPath;

    public DictionaryService(string configPath) { _configPath = configPath; }

    private MissionPaths? Mission()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return MissionLocator.Resolve(cfg);
    }

    private string? LimitsPath()
    {
        var mp = Mission();
        return mp is null ? null : Path.Combine(mp.MissionDir, "cfglimitsdefinition.xml");
    }

    private string? LimitsUserPath()
    {
        var mp = Mission();
        return mp is null ? null : Path.Combine(mp.MissionDir, "cfglimitsdefinitionuser.xml");
    }

    /// <summary>Read all valid names from <c>cfglimitsdefinition.xml</c>.
    /// Returns <see cref="LimitsDef.Empty"/> when the file is absent or unresolvable.</summary>
    public LimitsDef Load()
    {
        var path = LimitsPath();
        if (path is null || !File.Exists(path)) return LimitsDef.Empty;
        try { return LimitsXml.Parse(File.ReadAllText(path)); }
        catch { return LimitsDef.Empty; }
    }

    private (bool ok, string msg) EditLimits(Func<XDocument, bool> edit, string successMsg, string noOpMsg)
    {
        var path = LimitsPath();
        if (path is null) return (false, "no mission resolved for the active server");
        try
        {
            XDocument doc;
            if (File.Exists(path))
            {
                doc = LimitsXml.ParseDoc(File.ReadAllText(path));
            }
            else
            {
                doc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement("lists",
                        new XElement("categories"),
                        new XElement("tags"),
                        new XElement("usageflags"),
                        new XElement("valueflags")));
            }

            if (!edit(doc)) return (false, noOpMsg);
            CeBackup.Snapshot(path);
            File.WriteAllText(path, LimitsXml.ToXml(doc));
            return (true, successMsg);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Add a name to the given kind's list. No-op (returns false) if already present.</summary>
    public (bool ok, string msg) AddName(LimitsKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "name must not be empty");
        return EditLimits(
            doc => LimitsXml.AddName(doc, kind, name),
            $"added {kind} name '{name}'",
            $"{kind} name '{name}' already exists");
    }

    /// <summary>Remove a name from the given kind's list.</summary>
    public (bool ok, string msg) RemoveName(LimitsKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "name must not be empty");
        return EditLimits(
            doc => LimitsXml.RemoveName(doc, kind, name),
            $"removed {kind} name '{name}'",
            $"{kind} name '{name}' not found");
    }

    /// <summary>Rename a name in the given kind's list.</summary>
    public (bool ok, string msg) RenameName(LimitsKind kind, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return (false, "old name must not be empty");
        if (string.IsNullOrWhiteSpace(newName)) return (false, "new name must not be empty");
        return EditLimits(
            doc => LimitsXml.RenameName(doc, kind, oldName, newName),
            $"renamed {kind} name '{oldName}' → '{newName}'",
            $"rename failed: '{oldName}' not found or '{newName}' already exists");
    }

    /// <summary>Read all user groups from <c>cfglimitsdefinitionuser.xml</c>.
    /// Returns an empty list when the file is absent or unresolvable.</summary>
    public List<LimitsUserGroup> LoadGroups()
    {
        var path = LimitsUserPath();
        if (path is null || !File.Exists(path)) return new List<LimitsUserGroup>();
        try { return LimitsUserXml.Parse(File.ReadAllText(path)); }
        catch { return new List<LimitsUserGroup>(); }
    }

    // Upserts (edits that cannot no-op) route through the Func overload with an always-true edit.
    private (bool ok, string msg) EditLimitsUser(Action<XDocument> edit, string successMsg) =>
        EditLimitsUser(doc => { edit(doc); return true; }, successMsg, noOpMsg: "");

    private (bool ok, string msg) EditLimitsUser(Func<XDocument, bool> edit, string successMsg, string noOpMsg)
    {
        var path = LimitsUserPath();
        if (path is null) return (false, "no mission resolved for the active server");
        try
        {
            var doc = File.Exists(path)
                ? LimitsUserXml.ParseDoc(File.ReadAllText(path))
                : new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement("user_lists",
                        new XElement("usageflags"),
                        new XElement("valueflags")));

            if (!edit(doc)) return (false, noOpMsg);
            CeBackup.Snapshot(path);
            File.WriteAllText(path, LimitsUserXml.ToXml(doc));
            return (true, successMsg);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Add or replace a user group.</summary>
    public (bool ok, string msg) AddGroup(LimitsKind kind, string name, IReadOnlyList<string> members)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return EditLimitsUser(
            doc => LimitsUserXml.AddGroup(doc, kind, name, members),
            $"added/replaced {kind} group '{name}' with {members.Count} member(s)");
    }

    /// <summary>Remove a user group by name.</summary>
    public (bool ok, string msg) RemoveGroup(LimitsKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return EditLimitsUser(
            doc => LimitsUserXml.RemoveGroup(doc, kind, name),
            $"removed {kind} group '{name}'",
            $"{kind} group '{name}' not found");
    }

    /// <summary>Set (replace) the member list of an existing group, or create it if absent.</summary>
    public (bool ok, string msg) SetGroupMembers(LimitsKind kind, string name, IReadOnlyList<string> members)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return EditLimitsUser(
            doc => LimitsUserXml.SetGroupMembers(doc, kind, name, members),
            $"set {members.Count} member(s) on {kind} group '{name}'");
    }
}
