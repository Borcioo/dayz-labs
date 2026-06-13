using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active server instance's mission <c>cfgspawnabletypes.xml</c> — the per-type
/// hoarder flag, damage range, and cargo/attachments blocks (preset-based or inline chance-based).
/// Mirrors the <see cref="RandomPresetsService"/> pattern: one facade per frontend, never throws (returns
/// ok+message), snapshots a backup (<see cref="CeBackup"/>) before every write, edits in place so
/// comments/order survive a round-trip (<see cref="SpawnableTypesXml"/>).
/// </summary>
public sealed class SpawnableTypesService : CeFileService
{
    public SpawnableTypesService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgspawnabletypes.xml";
    protected override string? SeedRootName => "spawnabletypes";

    /// <summary>The mission's <c>cfgspawnabletypes.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? SpawnableTypesPath() => FilePath();

    /// <summary>Read all spawnable types. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<SpawnableType> Load() => LoadList(SpawnableTypesXml.Parse);

    // ------------------------------------------------------------------
    // Type-level edits
    // ------------------------------------------------------------------

    /// <summary>Add a new empty type. No-op when the name already exists (case-insensitive).</summary>
    public (bool ok, string msg) AddType(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "type name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.AddType(doc, name),
            $"added type '{name}'",
            $"type '{name}' already exists");
    }

    /// <summary>Remove a type by name.</summary>
    public (bool ok, string msg) RemoveType(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "type name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.RemoveType(doc, name),
            $"removed type '{name}'",
            $"type '{name}' not found");
    }

    /// <summary>Rename a type.</summary>
    public (bool ok, string msg) RenameType(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return (false, "old name must not be empty");
        if (string.IsNullOrWhiteSpace(newName)) return (false, "new name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.RenameType(doc, oldName, newName),
            $"renamed type '{oldName}' → '{newName}'",
            $"rename failed: '{oldName}' not found or '{newName}' already exists");
    }

    /// <summary>Set/clear the hoarder flag.</summary>
    public (bool ok, string msg) SetHoarder(string name, bool on)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "type name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.SetHoarder(doc, name, on),
            $"{(on ? "set" : "cleared")} hoarder on '{name}'",
            $"type '{name}' not found");
    }

    /// <summary>Set the damage range (both nulls clears the damage element).</summary>
    public (bool ok, string msg) SetDamage(string name, double? min, double? max)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "type name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.SetDamage(doc, name, min, max),
            min is null && max is null ? $"cleared damage on '{name}'" : $"set damage on '{name}'",
            $"type '{name}' not found");
    }

    // ------------------------------------------------------------------
    // Block-level edits
    // ------------------------------------------------------------------

    /// <summary>Add a block (preset-based when <paramref name="preset"/> is non-empty, else chance-based).</summary>
    public (bool ok, string msg) AddBlock(string typeName, bool isAttachments, string? preset, double? chance)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        var kind = isAttachments ? "attachments" : "cargo";
        return Edit(
            doc => SpawnableTypesXml.AddBlock(doc, typeName, isAttachments, preset, chance) >= 0,
            $"added {kind} block to '{typeName}'",
            $"type '{typeName}' not found");
    }

    /// <summary>Remove a block by kind + index.</summary>
    public (bool ok, string msg) RemoveBlock(string typeName, bool isAttachments, int index)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        var kind = isAttachments ? "attachments" : "cargo";
        return Edit(
            doc => SpawnableTypesXml.RemoveBlock(doc, typeName, isAttachments, index),
            $"removed {kind} block from '{typeName}'",
            $"{kind} block #{index} not found on '{typeName}'");
    }

    /// <summary>Convert a block to preset-based (strips chance + items).</summary>
    public (bool ok, string msg) SetBlockPreset(string typeName, bool isAttachments, int index, string preset)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        if (string.IsNullOrWhiteSpace(preset)) return (false, "preset name must not be empty");
        var kind = isAttachments ? "attachments" : "cargo";
        return Edit(
            doc => SpawnableTypesXml.SetBlockPreset(doc, typeName, isAttachments, index, preset),
            $"set preset on {kind} block of '{typeName}'",
            $"{kind} block #{index} not found on '{typeName}'");
    }

    /// <summary>Convert a block to chance-based (strips preset; keeps items).</summary>
    public (bool ok, string msg) SetBlockChance(string typeName, bool isAttachments, int index, double chance)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        var kind = isAttachments ? "attachments" : "cargo";
        return Edit(
            doc => SpawnableTypesXml.SetBlockChance(doc, typeName, isAttachments, index, chance),
            $"set chance on {kind} block of '{typeName}'",
            $"{kind} block #{index} not found on '{typeName}'");
    }

    // ------------------------------------------------------------------
    // Item-level edits (inside chance blocks)
    // ------------------------------------------------------------------

    /// <summary>Add an item to a chance block.</summary>
    public (bool ok, string msg) AddItem(string typeName, bool isAttachments, int index, string itemName, double chance)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.AddItem(doc, typeName, isAttachments, index, itemName, chance),
            $"added item '{itemName}'",
            $"item '{itemName}' not added (block missing or item already present)");
    }

    /// <summary>Remove an item from a chance block.</summary>
    public (bool ok, string msg) RemoveItem(string typeName, bool isAttachments, int index, string itemName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.RemoveItem(doc, typeName, isAttachments, index, itemName),
            $"removed item '{itemName}'",
            $"item '{itemName}' not found");
    }

    /// <summary>Update an item's chance, optionally renaming it.</summary>
    public (bool ok, string msg) SetItem(string typeName, bool isAttachments, int index,
                                         string itemName, double chance, string? newName = null)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return (false, "type name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => SpawnableTypesXml.SetItem(doc, typeName, isAttachments, index, itemName, chance, newName),
            $"updated item '{itemName}'",
            $"update failed for item '{itemName}'");
    }
}
