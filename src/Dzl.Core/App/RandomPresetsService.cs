using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active mission's <c>cfgrandompresets.xml</c> — the cargo and
/// attachments random presets referenced by name from <c>cfgspawnabletypes.xml</c>.
/// </summary>
public sealed class RandomPresetsService : CeFileService
{
    public RandomPresetsService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgrandompresets.xml";
    protected override string? SeedRootName => "randompresets";

    /// <summary>The mission's <c>cfgrandompresets.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? PresetsPath() => FilePath();

    /// <summary>Read all random presets. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<RandomPreset> Load() => LoadList(RandomPresetsXml.Parse);

    /// <summary>Add a new preset of the given kind. No-op when the name already exists for that kind.</summary>
    public (bool ok, string msg) AddPreset(PresetKind kind, string name, double chance)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.AddPreset(doc, kind, name, chance),
            $"added {kind} preset '{name}'",
            $"{kind} preset '{name}' already exists");
    }

    public (bool ok, string msg) RemovePreset(PresetKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.RemovePreset(doc, kind, name),
            $"removed {kind} preset '{name}'",
            $"{kind} preset '{name}' not found");
    }

    /// <summary>Disable a preset by commenting it out (kept for re-enabling, but the game won't load it).</summary>
    public (bool ok, string msg) DisablePreset(PresetKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.DisablePreset(doc, kind, name),
            $"disabled {kind} preset '{name}'",
            $"{kind} preset '{name}' not found, already disabled, or its content can't be commented");
    }

    /// <summary>Re-enable a previously disabled (commented) preset.</summary>
    public (bool ok, string msg) EnablePreset(PresetKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.EnablePreset(doc, kind, name),
            $"enabled {kind} preset '{name}'",
            $"no disabled {kind} preset '{name}' found");
    }

    /// <summary>Change a preset's kind (cargo ↔ attachments), keeping its name/chance/items.</summary>
    public (bool ok, string msg) SetPresetKind(PresetKind from, string name, PresetKind to)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.SetPresetKind(doc, from, name, to),
            $"changed '{name}' kind {from} → {to}",
            $"can't change kind: '{name}' not found or a {to} preset '{name}' already exists");
    }

    public (bool ok, string msg) RenamePreset(PresetKind kind, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return (false, "old name must not be empty");
        if (string.IsNullOrWhiteSpace(newName)) return (false, "new name must not be empty");
        return Edit(
            doc => RandomPresetsXml.RenamePreset(doc, kind, oldName, newName),
            $"renamed {kind} preset '{oldName}' → '{newName}'",
            $"rename failed: '{oldName}' not found or '{newName}' already exists");
    }

    /// <summary>Set a preset's chance (0..1).</summary>
    public (bool ok, string msg) SetPresetChance(PresetKind kind, string name, double chance)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.SetPresetChance(doc, kind, name, chance),
            $"set chance on {kind} preset '{name}'",
            $"{kind} preset '{name}' not found");
    }

    public (bool ok, string msg) AddItem(PresetKind kind, string presetName, string itemName, double chance)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return (false, "preset name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => RandomPresetsXml.AddItem(doc, kind, presetName, itemName, chance),
            $"added item '{itemName}' to {kind} preset '{presetName}'",
            $"item '{itemName}' not added (preset missing or item already present)");
    }

    public (bool ok, string msg) RemoveItem(PresetKind kind, string presetName, string itemName)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return (false, "preset name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => RandomPresetsXml.RemoveItem(doc, kind, presetName, itemName),
            $"removed item '{itemName}' from {kind} preset '{presetName}'",
            $"item '{itemName}' not found in {kind} preset '{presetName}'");
    }

    /// <summary>Update an item's chance, optionally renaming it.</summary>
    public (bool ok, string msg) SetItem(PresetKind kind, string presetName, string itemName,
                                         double chance, string? newName = null)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return (false, "preset name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => RandomPresetsXml.SetItem(doc, kind, presetName, itemName, chance, newName),
            $"updated item '{itemName}' in {kind} preset '{presetName}'",
            $"update failed for item '{itemName}' in {kind} preset '{presetName}'");
    }
}
