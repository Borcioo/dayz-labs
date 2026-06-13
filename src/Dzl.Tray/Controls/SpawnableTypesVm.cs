using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="SpawnableTypesEditor"/> control (the Economy "Spawnable Types" tab): a master list of
/// types from <c>cfgspawnabletypes.xml</c> plus a detail pane for the selected type (hoarder, damage, cargo and
/// attachments blocks). Each block is either preset-based (a name referencing <c>cfgrandompresets.xml</c>) or
/// chance-based (a chance + inline items). All edits route through <see cref="SpawnableTypesService"/> (never
/// throws; snapshots a backup before each write). Per-tab undo/redo + the status line come from
/// <see cref="RawXmlEditorVm"/>. The preset dropdowns are sourced from <see cref="RandomPresetsService"/>.
/// </summary>
public sealed partial class SpawnableTypesVm : RawXmlEditorVm
{
    private readonly SpawnableTypesService _svc;
    private readonly RandomPresetsService _presets;
    private readonly Func<string, bool> _confirm;
    private bool _suspendPersist;

    public SpawnableTypesVm(string configPath, Func<string, bool> confirm)
        : this(new SpawnableTypesService(configPath), configPath, confirm) { }

    private SpawnableTypesVm(SpawnableTypesService svc, string configPath, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.SpawnableTypesPath,
               "(no cfgspawnabletypes.xml — pick/scaffold a server mission)")
    {
        _svc = svc;
        _presets = new RandomPresetsService(configPath);
        _confirm = confirm;
    }

    private List<SpawnableType>? _model;

    /// <summary>The parsed file, re-read lazily after every write/undo/redo/reload.</summary>
    private List<SpawnableType> Model => _model ??= _svc.Load();

    /// <inheritdoc/>
    protected override void InvalidateModelCache() => _model = null;

    /// <summary>All types (unfiltered backing store).</summary>
    private readonly List<SpawnTypeRowVm> _all = new();

    /// <summary>The (filtered) master list shown in the grid.</summary>
    public ObservableCollection<SpawnTypeRowVm> Types { get; } = new();

    [ObservableProperty] private SpawnTypeRowVm? _selectedType;

    /// <summary>Cargo blocks of the selected type.</summary>
    public ObservableCollection<SpawnBlockVm> CargoBlocks { get; } = new();

    /// <summary>Attachments blocks of the selected type.</summary>
    public ObservableCollection<SpawnBlockVm> AttachmentsBlocks { get; } = new();

    [ObservableProperty] private string _filter = "";

    // new-type form
    [ObservableProperty] private string _newTypeName = "";

    // detail: hoarder + damage of selected type
    [ObservableProperty] private bool _selectedHoarder;
    [ObservableProperty] private string _damageMin = "";
    [ObservableProperty] private string _damageMax = "";

    /// <summary>Cargo preset names from cfgrandompresets.xml (for the add-block dropdown).</summary>
    public ObservableCollection<string> CargoPresetNames { get; } = new();

    /// <summary>Attachments preset names from cfgrandompresets.xml (for the add-block dropdown).</summary>
    public ObservableCollection<string> AttachmentsPresetNames { get; } = new();

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>(Re)load all types from disk. Also refreshes the preset-name dropdowns (which undo/redo
    /// deliberately leave alone — they belong to cfgrandompresets.xml, not this file).</summary>
    public override void Reload()
    {
        LoadPresetNames();
        base.Reload();
    }

    /// <inheritdoc/>
    protected override void ReloadView()
    {
        LoadTypesKeepingSelection();
        LoadDetailForSelected();
    }

    private void LoadPresetNames()
    {
        CargoPresetNames.Clear();
        AttachmentsPresetNames.Clear();
        foreach (var p in _presets.Load().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (p.Kind == PresetKind.Cargo) CargoPresetNames.Add(p.Name);
            else AttachmentsPresetNames.Add(p.Name);
        }
    }

    private void LoadTypesKeepingSelection()
    {
        var prevName = SelectedType?.Name;

        _all.Clear();
        foreach (var t in Model.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            _all.Add(new SpawnTypeRowVm(t));

        ApplyFilter();

        SelectedType = Types.FirstOrDefault(r => string.Equals(r.Name, prevName, StringComparison.OrdinalIgnoreCase))
                       ?? Types.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var prev = SelectedType;
        Types.Clear();
        var f = (Filter ?? "").Trim();
        IEnumerable<SpawnTypeRowVm> rows = _all;
        if (!string.IsNullOrEmpty(f))
            rows = rows.Where(r => !string.IsNullOrEmpty(r.Name) &&
                r.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        foreach (var r in rows) Types.Add(r);

        if (prev is not null && Types.Contains(prev)) SelectedType = prev;
    }

    partial void OnSelectedTypeChanged(SpawnTypeRowVm? value) => LoadDetailForSelected();

    private SpawnableType? FindModel(string name) =>
        Model.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private void LoadDetailForSelected()
    {
        _suspendPersist = true;
        try
        {
            DetachBlocks(CargoBlocks);
            DetachBlocks(AttachmentsBlocks);
            CargoBlocks.Clear();
            AttachmentsBlocks.Clear();

            if (SelectedType is { } row && FindModel(row.Name) is { } model)
            {
                SelectedHoarder = model.Hoarder;
                DamageMin = model.DamageMin?.ToString(CultureInfo.InvariantCulture) ?? "";
                DamageMax = model.DamageMax?.ToString(CultureInfo.InvariantCulture) ?? "";

                for (var i = 0; i < model.Cargo.Count; i++)
                    AddBlockVm(CargoBlocks, model.Cargo[i], i, isAttachments: false);
                for (var i = 0; i < model.Attachments.Count; i++)
                    AddBlockVm(AttachmentsBlocks, model.Attachments[i], i, isAttachments: true);
            }
            else
            {
                SelectedHoarder = false;
                DamageMin = "";
                DamageMax = "";
            }
        }
        finally { _suspendPersist = false; }
    }

    private void AddBlockVm(ObservableCollection<SpawnBlockVm> dest, SpawnBlock block, int index, bool isAttachments)
    {
        var vm = new SpawnBlockVm(block, index, isAttachments);
        vm.PresetEdited += OnBlockPresetEdited;
        vm.ChanceEdited += OnBlockChanceEdited;
        vm.ItemEdited += OnItemEdited;
        dest.Add(vm);
    }

    private void DetachBlocks(ObservableCollection<SpawnBlockVm> blocks)
    {
        foreach (var b in blocks)
        {
            b.PresetEdited -= OnBlockPresetEdited;
            b.ChanceEdited -= OnBlockChanceEdited;
            b.ItemEdited -= OnItemEdited;
        }
    }

    /// <summary>Parse an optional damage field: empty → null; otherwise must be 0..1.</summary>
    private static bool TryDamage(string raw, out double? value)
    {
        value = null;
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return true;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v is >= 0.0 and <= 1.0)
        {
            value = v;
            return true;
        }
        return false;
    }

    public void AddType()
    {
        var name = (NewTypeName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ type name must not be empty"; return; }

        PushUndo();
        if (Report(_svc.AddType(name)))
        {
            NewTypeName = "";
            LoadTypesKeepingSelection();
            SelectedType = Types.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveSelectedType()
    {
        if (SelectedType is not { } row) { Status = "✗ select a type to remove"; return; }
        if (!_confirm($"Remove the spawnable type \"{row.Name}\" and all its blocks?")) return;
        PushUndo();
        if (Report(_svc.RemoveType(row.Name))) LoadTypesKeepingSelection();
    }

    public void RenameSelectedType(string newName)
    {
        if (SelectedType is not { } row) { Status = "✗ select a type to rename"; return; }
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ new name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.RenameType(row.Name, newName)))
        {
            LoadTypesKeepingSelection();
            SelectedType = Types.FirstOrDefault(r => string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Persist the hoarder toggle for the selected type.</summary>
    public void SaveHoarder()
    {
        if (_suspendPersist || SelectedType is not { } row) return;
        PushUndo();
        if (Report(_svc.SetHoarder(row.Name, SelectedHoarder))) { row.Hoarder = SelectedHoarder; }
    }

    /// <summary>Persist the damage min/max for the selected type (both empty clears it).</summary>
    public void SaveDamage()
    {
        if (_suspendPersist || SelectedType is not { } row) return;
        if (!TryDamage(DamageMin, out var min)) { Status = "✗ damage min must be empty or a number 0..1"; return; }
        if (!TryDamage(DamageMax, out var max)) { Status = "✗ damage max must be empty or a number 0..1"; return; }
        PushUndo();
        if (Report(_svc.SetDamage(row.Name, min, max))) row.DamageLabel = SpawnTypeRowVm.FormatDamage(min, max);
    }

    /// <summary>Add a chance-based block (chance 1.0, no items) to cargo or attachments.</summary>
    public void AddChanceBlock(bool isAttachments)
    {
        if (SelectedType is not { } row) { Status = "✗ select a type first"; return; }
        PushUndo();
        if (Report(_svc.AddBlock(row.Name, isAttachments, preset: null, chance: 1.0)))
            AfterBlockChange(row, isAttachments);
    }

    /// <summary>Add a preset-based block referencing <paramref name="preset"/>.</summary>
    public void AddPresetBlock(bool isAttachments, string preset)
    {
        if (SelectedType is not { } row) { Status = "✗ select a type first"; return; }
        preset = (preset ?? "").Trim();
        if (preset.Length == 0) { Status = "✗ pick a preset"; return; }
        PushUndo();
        if (Report(_svc.AddBlock(row.Name, isAttachments, preset: preset, chance: null)))
            AfterBlockChange(row, isAttachments);
    }

    public void RemoveBlock(SpawnBlockVm? block)
    {
        if (SelectedType is not { } row || block is null) { Status = "✗ select a block to remove"; return; }
        PushUndo();
        if (Report(_svc.RemoveBlock(row.Name, block.IsAttachments, block.Index)))
            AfterBlockChange(row, block.IsAttachments);
    }

    /// <summary>Switch a block to preset-based with the given preset.</summary>
    public void SetBlockPreset(SpawnBlockVm block, string preset)
    {
        if (SelectedType is not { } row) return;
        preset = (preset ?? "").Trim();
        if (preset.Length == 0) { Status = "✗ pick a preset"; return; }
        PushUndo();
        if (Report(_svc.SetBlockPreset(row.Name, block.IsAttachments, block.Index, preset)))
            AfterBlockChange(row, block.IsAttachments);
    }

    /// <summary>Switch a block to chance-based with the given chance.</summary>
    public void SetBlockChance(SpawnBlockVm block, string chanceText)
    {
        if (SelectedType is not { } row) return;
        if (!TryChance(chanceText, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }
        PushUndo();
        if (Report(_svc.SetBlockChance(row.Name, block.IsAttachments, block.Index, chance)))
            AfterBlockChange(row, block.IsAttachments);
    }

    private void OnBlockPresetEdited(SpawnBlockVm block)
    {
        if (_suspendPersist) return;
        SetBlockPreset(block, block.PresetText);
    }

    private void OnBlockChanceEdited(SpawnBlockVm block)
    {
        if (_suspendPersist) return;
        SetBlockChance(block, block.ChanceText);
    }

    public void AddItem(SpawnBlockVm? block, string itemName, string chanceText)
    {
        if (SelectedType is not { } row || block is null) { Status = "✗ select a block first"; return; }
        itemName = (itemName ?? "").Trim();
        if (itemName.Length == 0) { Status = "✗ item name must not be empty"; return; }
        if (!TryChance(chanceText, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }
        PushUndo();
        if (Report(_svc.AddItem(row.Name, block.IsAttachments, block.Index, itemName, chance)))
            AfterBlockChange(row, block.IsAttachments);
    }

    public void RemoveItem(SpawnBlockVm? block, SpawnItemVm? item)
    {
        if (SelectedType is not { } row || block is null || item is null) { Status = "✗ select an item to remove"; return; }
        PushUndo();
        if (Report(_svc.RemoveItem(row.Name, block.IsAttachments, block.Index, item.Name)))
            AfterBlockChange(row, block.IsAttachments);
    }

    private void OnItemEdited(SpawnBlockVm block, SpawnItemVm item)
    {
        if (_suspendPersist || SelectedType is not { } row) return;
        if (string.IsNullOrWhiteSpace(item.Name)) { Status = "✗ item name must not be empty"; return; }
        if (!TryChance(item.ChanceText, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }
        PushUndo();
        Report(_svc.SetItem(row.Name, block.IsAttachments, block.Index, item.OriginalName, chance, item.Name));
        AfterBlockChange(row, block.IsAttachments);
    }

    /// <summary>After any block/item mutation: reload detail (re-indexes blocks) and refresh the row counts.</summary>
    private void AfterBlockChange(SpawnTypeRowVm row, bool _)
    {
        if (FindModel(row.Name) is { } model)
        {
            row.CargoCount = model.Cargo.Count;
            row.AttachmentsCount = model.Attachments.Count;
        }
        LoadDetailForSelected();
    }
}

/// <summary>One master-list row: a spawnable type's name, hoarder flag, damage label and block counts.</summary>
public sealed partial class SpawnTypeRowVm : ObservableObject
{
    public SpawnTypeRowVm(SpawnableType t)
    {
        Name = t.Name;
        _hoarder = t.Hoarder;
        _damageLabel = FormatDamage(t.DamageMin, t.DamageMax);
        _cargoCount = t.Cargo.Count;
        _attachmentsCount = t.Attachments.Count;
    }

    public string Name { get; }

    [ObservableProperty] private bool _hoarder;
    [ObservableProperty] private string _damageLabel;
    [ObservableProperty] private int _cargoCount;
    [ObservableProperty] private int _attachmentsCount;

    public static string FormatDamage(double? min, double? max)
    {
        if (min is null && max is null) return "";
        var lo = min?.ToString(CultureInfo.InvariantCulture) ?? "?";
        var hi = max?.ToString(CultureInfo.InvariantCulture) ?? "?";
        return $"{lo}–{hi}";
    }
}

/// <summary>One cargo/attachments block in the detail pane. Either preset-based (<see cref="IsPreset"/>) or
/// chance-based with inline <see cref="Items"/>. <see cref="Index"/> is its position within its kind on the
/// selected type (used to address it for edits).</summary>
public sealed partial class SpawnBlockVm : ObservableObject
{
    public SpawnBlockVm(SpawnBlock block, int index, bool isAttachments)
    {
        Index = index;
        IsAttachments = isAttachments;
        _isPreset = block.IsPreset;
        _presetText = block.Preset ?? "";
        _chanceText = block.Chance?.ToString(CultureInfo.InvariantCulture) ?? "1.0";
        foreach (var i in block.Items)
        {
            var ivm = new SpawnItemVm(i.Name, i.Chance);
            ivm.Edited += it => ItemEdited?.Invoke(this, it);
            Items.Add(ivm);
        }
    }

    public int Index { get; }
    public bool IsAttachments { get; }

    [ObservableProperty] private bool _isPreset;

    /// <summary>True when this block is chance-based (inverse of <see cref="IsPreset"/>), for visibility binds.</summary>
    public bool IsChance => !IsPreset;

    partial void OnIsPresetChanged(bool value) => OnPropertyChanged(nameof(IsChance));

    /// <summary>Preset name (when preset-based). Bound to the preset combo; commit raises <see cref="PresetEdited"/>.</summary>
    [ObservableProperty] private string _presetText;

    /// <summary>Chance text (when chance-based). Commit raises <see cref="ChanceEdited"/>.</summary>
    [ObservableProperty] private string _chanceText;

    public ObservableCollection<SpawnItemVm> Items { get; } = new();

    /// <summary>One-line summary for collapsed/compact display.</summary>
    public string Summary => IsPreset
        ? $"preset: {PresetText}"
        : $"chance {ChanceText} · {Items.Count} item(s)";

    public event Action<SpawnBlockVm>? PresetEdited;
    public event Action<SpawnBlockVm>? ChanceEdited;
    public event Action<SpawnBlockVm, SpawnItemVm>? ItemEdited;

    public void CommitPreset() => PresetEdited?.Invoke(this);
    public void CommitChance() => ChanceEdited?.Invoke(this);
}

/// <summary>One editable item row (Name + Chance) inside a chance block.</summary>
public sealed partial class SpawnItemVm : ObservableObject
{
    public SpawnItemVm(string name, double chance)
    {
        _name = name;
        OriginalName = name;
        _chance = chance;
        _chanceText = chance.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The item name as last persisted (used to locate the element when renaming).</summary>
    public string OriginalName { get; private set; }

    public event Action<SpawnItemVm>? Edited;

    [ObservableProperty] private string _name;
    [ObservableProperty] private double _chance;
    [ObservableProperty] private string _chanceText;

    public void Commit() => Edited?.Invoke(this);
    public void CommitName() => OriginalName = Name;
}
