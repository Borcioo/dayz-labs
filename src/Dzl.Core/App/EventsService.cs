using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active server instance's mission <c>db/events.xml</c> — the CE spawn events
/// list (each event drives a spawner: nominal counts, radii, flags, child types).
/// Mirrors the <see cref="RandomPresetsService"/> pattern: one facade per frontend, never throws (returns
/// ok+message), snapshots a backup (<see cref="CeBackup"/>) before every write, edits in place so
/// comments/order survive a round-trip (<see cref="EventsXml"/>).
/// </summary>
public sealed class EventsService : CeFileService
{
    public EventsService(string configPath) : base(configPath) { }

    protected override string RelativePath => Path.Combine("db", "events.xml");
    protected override string? SeedRootName => "events";

    /// <summary>The mission's <c>db/events.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? EventsPath() => FilePath();

    /// <summary>Read all CE events. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<CeEvent> Load() => LoadList(EventsXml.Parse);

    // ------------------------------------------------------------------
    // Event-level edits
    // ------------------------------------------------------------------

    /// <summary>Add a new event with default structure. No-op when the name already exists.</summary>
    public (bool ok, string msg) AddEvent(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.AddEvent(doc, name),
            $"added event '{name}'",
            $"event '{name}' already exists");
    }

    /// <summary>Remove an event by name.</summary>
    public (bool ok, string msg) RemoveEvent(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.RemoveEvent(doc, name),
            $"removed event '{name}'",
            $"event '{name}' not found");
    }

    /// <summary>Rename an event in place (preserves children/structure).</summary>
    public (bool ok, string msg) RenameEvent(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return (false, "old name must not be empty");
        if (string.IsNullOrWhiteSpace(newName)) return (false, "new name must not be empty");
        return Edit(
            doc => EventsXml.RenameEvent(doc, oldName, newName),
            $"renamed event '{oldName}' → '{newName}'",
            $"rename failed: '{oldName}' not found or '{newName}' already exists");
    }

    /// <summary>Set an integer scalar on an event. Field names: nominal|min|max|lifetime|restock|saferadius|distanceradius|cleanupradius.</summary>
    public (bool ok, string msg) SetScalar(string eventName, string field, int value)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.SetScalar(doc, eventName, field, value),
            $"set {field}={value} on event '{eventName}'",
            $"event '{eventName}' not found");
    }

    /// <summary>Set a flag on an event. Flag names: deletable|init_random|remove_damaged.</summary>
    public (bool ok, string msg) SetFlag(string eventName, string flag, bool value)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.SetFlag(doc, eventName, flag, value),
            $"set flag {flag}={value} on event '{eventName}'",
            $"event '{eventName}' not found");
    }

    /// <summary>Set the position string on an event.</summary>
    public (bool ok, string msg) SetPosition(string eventName, string position)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.SetPosition(doc, eventName, position),
            $"set position='{position}' on event '{eventName}'",
            $"event '{eventName}' not found");
    }

    /// <summary>Set the limit string on an event.</summary>
    public (bool ok, string msg) SetLimit(string eventName, string limit)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.SetLimit(doc, eventName, limit),
            $"set limit='{limit}' on event '{eventName}'",
            $"event '{eventName}' not found");
    }

    /// <summary>Set the active flag on an event.</summary>
    public (bool ok, string msg) SetActive(string eventName, bool active)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        return Edit(
            doc => EventsXml.SetActive(doc, eventName, active),
            $"set active={active} on event '{eventName}'",
            $"event '{eventName}' not found");
    }

    // ------------------------------------------------------------------
    // Child-level edits
    // ------------------------------------------------------------------

    /// <summary>Add a child entry to an event.</summary>
    public (bool ok, string msg) AddChild(string eventName, EventChild child)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        if (string.IsNullOrWhiteSpace(child.Type)) return (false, "child type must not be empty");
        return Edit(
            doc => EventsXml.AddChild(doc, eventName, child),
            $"added child '{child.Type}' to event '{eventName}'",
            $"child not added (event missing or child type already present)");
    }

    /// <summary>Remove a child by type from an event.</summary>
    public (bool ok, string msg) RemoveChild(string eventName, string type)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        if (string.IsNullOrWhiteSpace(type)) return (false, "child type must not be empty");
        return Edit(
            doc => EventsXml.RemoveChild(doc, eventName, type),
            $"removed child '{type}' from event '{eventName}'",
            $"child '{type}' not found in event '{eventName}'");
    }

    /// <summary>Update a child entry in place (set numeric fields, optionally rename type).</summary>
    public (bool ok, string msg) SetChild(string eventName, string type, EventChild updated)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return (false, "event name must not be empty");
        if (string.IsNullOrWhiteSpace(type)) return (false, "child type must not be empty");
        return Edit(
            doc => EventsXml.SetChild(doc, eventName, type, updated),
            $"updated child '{type}' in event '{eventName}'",
            $"update failed for child '{type}' in event '{eventName}'");
    }
}
