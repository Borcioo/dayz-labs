using System.Xml.Linq;
using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>
/// Facade for editing the active server instance's mission <c>cfgrandompresets.xml</c> — the cargo and
/// attachments random presets referenced by name from <c>cfgspawnabletypes.xml</c>.
/// Mirrors the <see cref="DictionaryService"/> pattern: one facade per frontend, never throws (returns
/// ok+message), snapshots a backup (<see cref="CeBackup"/>) before every write, edits in place so
/// comments/order survive a round-trip (<see cref="RandomPresetsXml"/>).
/// </summary>
public sealed class RandomPresetsService
{
    private readonly string _configPath;

    public RandomPresetsService(string configPath) { _configPath = configPath; }

    // ------------------------------------------------------------------
    // Path resolution
    // ------------------------------------------------------------------

    private MissionPaths? Mission()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return MissionLocator.Resolve(cfg);
    }

    /// <summary>The mission's <c>cfgrandompresets.xml</c> path (whether or not it exists yet),
    /// or null when no mission is resolvable.</summary>
    public string? PresetsPath()
    {
        var mp = Mission();
        return mp is null ? null : Path.Combine(mp.MissionDir, "cfgrandompresets.xml");
    }

    // ------------------------------------------------------------------
    // Read
    // ------------------------------------------------------------------

    /// <summary>Read all random presets. Returns an empty list when the file is absent or unresolvable.</summary>
    public List<RandomPreset> Load()
    {
        var path = PresetsPath();
        if (path is null || !File.Exists(path)) return new List<RandomPreset>();
        try { return RandomPresetsXml.Parse(File.ReadAllText(path)); }
        catch { return new List<RandomPreset>(); }
    }

    /// <summary>Raw current file text (or null when absent/unresolvable). Used by the tray's per-tab
    /// undo/redo, which snapshots the whole file before each edit and restores it verbatim.</summary>
    public string? ReadRaw()
    {
        var path = PresetsPath();
        if (path is null || !File.Exists(path)) return null;
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    /// <summary>Overwrite the file with <paramref name="xml"/> verbatim (snapshots a backup first).
    /// Used by undo/redo. Never throws.</summary>
    public (bool ok, string msg) WriteRaw(string xml)
    {
        var path = PresetsPath();
        if (path is null) return (false, "no mission resolved for the active server");
        try
        {
            CeBackup.Snapshot(path);
            File.WriteAllText(path, xml);
            return (true, "restored");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ------------------------------------------------------------------
    // Write helper
    // ------------------------------------------------------------------

    private (bool ok, string msg) Edit(Func<XDocument, bool> edit, string successMsg, string noOpMsg)
    {
        var path = PresetsPath();
        if (path is null) return (false, "no mission resolved for the active server");
        try
        {
            var doc = File.Exists(path)
                ? RandomPresetsXml.ParseDoc(File.ReadAllText(path))
                : new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("randompresets"));

            if (!edit(doc)) return (false, noOpMsg);
            CeBackup.Snapshot(path);
            File.WriteAllText(path, RandomPresetsXml.ToXml(doc));
            return (true, successMsg);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ------------------------------------------------------------------
    // Preset-level edits
    // ------------------------------------------------------------------

    /// <summary>Add a new preset of the given kind. No-op when the name already exists for that kind.</summary>
    public (bool ok, string msg) AddPreset(PresetKind kind, string name, double chance)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.AddPreset(doc, kind, name, chance),
            $"added {kind} preset '{name}'",
            $"{kind} preset '{name}' already exists");
    }

    /// <summary>Remove a preset by kind + name.</summary>
    public (bool ok, string msg) RemovePreset(PresetKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "preset name must not be empty");
        return Edit(
            doc => RandomPresetsXml.RemovePreset(doc, kind, name),
            $"removed {kind} preset '{name}'",
            $"{kind} preset '{name}' not found");
    }

    /// <summary>Rename a preset.</summary>
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

    // ------------------------------------------------------------------
    // Item-level edits
    // ------------------------------------------------------------------

    /// <summary>Add an item to a preset.</summary>
    public (bool ok, string msg) AddItem(PresetKind kind, string presetName, string itemName, double chance)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return (false, "preset name must not be empty");
        if (string.IsNullOrWhiteSpace(itemName)) return (false, "item name must not be empty");
        return Edit(
            doc => RandomPresetsXml.AddItem(doc, kind, presetName, itemName, chance),
            $"added item '{itemName}' to {kind} preset '{presetName}'",
            $"item '{itemName}' not added (preset missing or item already present)");
    }

    /// <summary>Remove an item from a preset.</summary>
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
