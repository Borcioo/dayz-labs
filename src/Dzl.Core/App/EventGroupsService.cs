using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Facade for editing the active mission's <c>cfgeventgroups.xml</c> — named groups of objects an
/// event spawns together.</summary>
public sealed class EventGroupsService : CeFileService
{
    public EventGroupsService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgeventgroups.xml";
    protected override string? SeedRootName => "eventgroupdef";

    public string? EventGroupsPath() => FilePath();

    public List<EventGroup> Load() => LoadList(EventGroupsXml.Parse);

    public (bool ok, string msg) AddGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return Edit(doc => EventGroupsXml.AddGroup(doc, name), $"added group '{name}'", $"group '{name}' already exists");
    }

    public (bool ok, string msg) RemoveGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "group name must not be empty");
        return Edit(doc => EventGroupsXml.RemoveGroup(doc, name), $"removed group '{name}'", $"group '{name}' not found");
    }

    public (bool ok, string msg) AddChild(string group, string type, double x, double y, double z, double a, int lootMin, int lootMax, bool deloot)
    {
        if (string.IsNullOrWhiteSpace(type)) return (false, "child type must not be empty");
        return Edit(doc => EventGroupsXml.AddChild(doc, group, type, x, y, z, a, lootMin, lootMax, deloot),
            $"added child to '{group}'", $"group '{group}' not found");
    }

    public (bool ok, string msg) RemoveChild(string group, int index) =>
        Edit(doc => EventGroupsXml.RemoveChild(doc, group, index), $"removed child #{index} from '{group}'", "child not removed");

    public (bool ok, string msg) SetChild(string group, int index, string type, double x, double y, double z, double a, int lootMin, int lootMax, bool deloot)
    {
        if (string.IsNullOrWhiteSpace(type)) return (false, "child type must not be empty");
        return Edit(doc => EventGroupsXml.SetChild(doc, group, index, type, x, y, z, a, lootMin, lootMax, deloot),
            $"updated child #{index} in '{group}'", "child not updated");
    }
}
