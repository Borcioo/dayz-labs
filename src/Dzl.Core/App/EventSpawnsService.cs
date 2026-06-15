using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Facade for editing the active mission's <c>cfgeventspawns.xml</c> — per dynamic-event spawn positions.</summary>
public sealed class EventSpawnsService : CeFileService
{
    public EventSpawnsService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgeventspawns.xml";
    protected override string? SeedRootName => "eventposdef";

    public string? EventSpawnsPath() => FilePath();

    public List<EventSpawn> Load() => LoadList(EventSpawnsXml.Parse);

    public (bool ok, string msg) AddEvent(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "event name must not be empty");
        return Edit(doc => EventSpawnsXml.AddEvent(doc, name), $"added event '{name}'", $"event '{name}' already exists");
    }

    public (bool ok, string msg) RemoveEvent(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "event name must not be empty");
        return Edit(doc => EventSpawnsXml.RemoveEvent(doc, name), $"removed event '{name}'", $"event '{name}' not found");
    }

    public (bool ok, string msg) AddPos(string eventName, double x, double z, double a)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event must not be empty");
        return Edit(doc => EventSpawnsXml.AddPos(doc, eventName, x, z, a), $"added pos to '{eventName}'", $"event '{eventName}' not found");
    }

    public (bool ok, string msg) RemovePos(string eventName, int index)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event must not be empty");
        return Edit(doc => EventSpawnsXml.RemovePos(doc, eventName, index), $"removed pos #{index} from '{eventName}'", "pos not removed");
    }

    public (bool ok, string msg) SetPos(string eventName, int index, double x, double z, double a)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event must not be empty");
        return Edit(doc => EventSpawnsXml.SetPos(doc, eventName, index, x, z, a), $"updated pos #{index} in '{eventName}'", "pos not updated");
    }
}
