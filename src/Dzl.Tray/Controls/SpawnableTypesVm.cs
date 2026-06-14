using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
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
    private readonly TypesService _types;
    private readonly Func<string, bool> _confirm;
    private bool _suspendPersist;

    public SpawnableTypesVm(string configPath, Func<string, bool> confirm)
        : this(new SpawnableTypesService(configPath), configPath, confirm) { }

    private SpawnableTypesVm(SpawnableTypesService svc, string configPath, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.SpawnableTypesPath,
               "(no cfgspawnabletypes.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _presets = new RandomPresetsService(configPath);
        _types = new TypesService(configPath);
        _confirm = confirm;
    }

    /// <summary>Distinct item classnames from every Types file of the mission — the suggestion pool for the
    /// chance-block add-item box. Cached; refreshed on <see cref="Reload"/>. Not a hard allowlist: an item
    /// may legitimately reference a class outside types.xml, so free text stays valid — this only powers
    /// autocomplete.</summary>
    public ObservableCollection<string> TypeNames { get; } = new();

    private void LoadTypeNames()
    {
        TypeNames.Clear();
        foreach (var n in _types.List().Select(e => e.Name)
                     .Where(n => !string.IsNullOrEmpty(n))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            TypeNames.Add(n);
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

    /// <summary>True when a type is selected — gates the inline rename row in the detail header.</summary>
    public bool HasSelectedType => SelectedType is not null;

    /// <summary>Editable copy of the selected type's name for the inline rename box (synced on selection,
    /// committed via <see cref="CommitRename"/> — replaces the old modal rename dialog).</summary>
    [ObservableProperty] private string _renameText = "";

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

    partial void OnFilterChanged(string value) => ScheduleFilter();

    // Debounce the master-list filter: rebuilding the (potentially thousands of) rows on every keystroke
    // stutters, so coalesce a burst of typing into one ApplyFilter ~200 ms after the last change.
    private DispatcherTimer? _filterTimer;

    private void ScheduleFilter()
    {
        _filterTimer ??= CreateFilterTimer();
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private DispatcherTimer CreateFilterTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        t.Tick += (_, _) => { t.Stop(); ApplyFilter(); };
        return t;
    }

    /// <summary>Apply the filter immediately, cancelling any pending debounce — used by tests and by code
    /// (e.g. <see cref="SelectByEntry"/>) that needs the filtered list current synchronously.</summary>
    public void ApplyFilterNow()
    {
        _filterTimer?.Stop();
        ApplyFilter();
    }

    /// <summary>(Re)load all types from disk. Also refreshes the preset-name dropdowns (which undo/redo
    /// deliberately leave alone — they belong to cfgrandompresets.xml, not this file).</summary>
    public override void Reload()
    {
        LoadPresetNames();
        LoadTypeNames();
        base.Reload();
    }

    /// <summary>Refresh ONLY the cross-file reference pools (preset names from cfgrandompresets.xml, item
    /// classnames from types.xml) without reloading this file's model — so switching INTO this tab picks up
    /// edits made on the Random Presets / Types tabs, while keeping the current selection + undo history.</summary>
    public void RefreshReferences()
    {
        LoadPresetNames();
        LoadTypeNames();
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
            rows = rows.Where(r => r.SearchText.Contains(f, StringComparison.OrdinalIgnoreCase));
        foreach (var r in rows) Types.Add(r);

        if (prev is not null && Types.Contains(prev)) SelectedType = prev;
    }

    /// <summary>Select the type named <paramref name="name"/> (e.g. from a dashboard finding click), clearing
    /// the filter first only if it would otherwise hide the row. Selects the entry directly — it does NOT
    /// narrow the list to a filter.</summary>
    public void SelectByEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!Types.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Filter = "";
            ApplyFilterNow();   // the filter is debounced — unfilter now so the target row is present to select
        }
        SelectedType = Types.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedTypeChanged(SpawnTypeRowVm? value)
    {
        OnPropertyChanged(nameof(HasSelectedType));
        LoadDetailForSelected();
    }

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
            RenameText = SelectedType?.Name ?? "";

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
        var presetSuggestions = isAttachments ? AttachmentsPresetNames : CargoPresetNames;
        var vm = new SpawnBlockVm(block, index, isAttachments, presetSuggestions, TypeNames);
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

    /// <summary>Commit the inline rename box (the detail-header name field) — renames the selected type.</summary>
    public void CommitRename() => RenameSelectedType(RenameText);

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
        SetBlockChance(block, block.Chance.ToString(CultureInfo.InvariantCulture));
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
        var chance = Math.Clamp(item.Chance, 0, 1);
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
        SearchText = BuildSearchText(t);
        _hoarder = t.Hoarder;
        _damageLabel = FormatDamage(t.DamageMin, t.DamageMax);
        _cargoCount = t.Cargo.Count;
        _attachmentsCount = t.Attachments.Count;
    }

    public string Name { get; }

    /// <summary>Haystack for the master-list search: the type name PLUS every referenced preset name and every
    /// inline item classname across its cargo/attachments blocks — so the filter finds a type by what it
    /// contains (a preset it uses, an item it spawns), not only by its own name.</summary>
    public string SearchText { get; }

    private static string BuildSearchText(SpawnableType t)
    {
        var parts = new List<string> { t.Name };
        foreach (var b in t.Cargo.Concat(t.Attachments))
        {
            if (!string.IsNullOrEmpty(b.Preset)) parts.Add(b.Preset!);
            foreach (var i in b.Items) parts.Add(i.Name);
        }
        return string.Join(" ", parts);
    }

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
    public SpawnBlockVm(SpawnBlock block, int index, bool isAttachments,
                        ObservableCollection<string> presetSuggestions, IReadOnlyList<string> itemPool)
    {
        Index = index;
        IsAttachments = isAttachments;
        PresetSuggestions = presetSuggestions;
        ItemPool = itemPool;
        _isPreset = block.IsPreset;
        _presetText = block.Preset ?? "";
        _chanceText = block.Chance?.ToString(CultureInfo.InvariantCulture) ?? "1.0";
        _chance = block.Chance ?? 1.0;
        foreach (var i in block.Items)
        {
            var ivm = new SpawnItemVm(i.Name, i.Chance);
            ivm.Edited += it => ItemEdited?.Invoke(this, it);
            Items.Add(ivm);
        }
    }

    public int Index { get; }
    public bool IsAttachments { get; }

    /// <summary>Preset-name autocomplete pool for this block's kind (cargo or attachments), from the parent VM.</summary>
    public ObservableCollection<string> PresetSuggestions { get; }

    /// <summary>The classname pool (the mission's Types classnames) the add-item box filters for suggestions.
    /// Bound to the reusable <see cref="AutoSuggestBox"/>, which owns the filter/open/pick behaviour.</summary>
    public IReadOnlyList<string> ItemPool { get; }

    /// <summary>Bound to the add-item box's text; the classname to add (cleared on a successful add).</summary>
    [ObservableProperty] private string _newItemName = "";

    [ObservableProperty] private bool _isPreset;

    /// <summary>True when this block is chance-based (inverse of <see cref="IsPreset"/>), for visibility binds.</summary>
    public bool IsChance => !IsPreset;

    /// <summary>Header label: "Preset block" or "Chance block" (drives the block's type pill).</summary>
    public string BlockKindLabel => IsPreset ? "Preset block" : "Chance block";

    partial void OnIsPresetChanged(bool value)
    {
        OnPropertyChanged(nameof(IsChance));
        OnPropertyChanged(nameof(BlockKindLabel));
    }

    /// <summary>Preset name (when preset-based). Bound to the preset combo; commit raises <see cref="PresetEdited"/>.</summary>
    [ObservableProperty] private string _presetText;

    /// <summary>Chance text (when chance-based). Commit raises <see cref="ChanceEdited"/>.</summary>
    [ObservableProperty] private string _chanceText;

    /// <summary>Chance as a number (when chance-based) — bound by the ChanceField; commit raises <see cref="ChanceEdited"/>.</summary>
    [ObservableProperty] private double _chance;

    /// <summary>Chance for the add-item row of this block (bound by its ChanceField).</summary>
    [ObservableProperty] private double _newItemChance = 1.0;

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
