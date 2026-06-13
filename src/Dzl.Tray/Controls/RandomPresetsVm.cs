using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="RandomPresetsEditor"/> control (the Economy "Random Presets" tab): a master list of
/// cargo/attachments presets from <c>cfgrandompresets.xml</c> plus the item grid of the selected preset.
/// All edits route through <see cref="RandomPresetsService"/> (never throws; snapshots a backup before each
/// write). Per-tab undo/redo + the status line come from <see cref="RawXmlEditorVm"/>. Self-contained so it
/// doesn't bloat MainViewModel.
/// </summary>
public sealed partial class RandomPresetsVm : RawXmlEditorVm
{
    private readonly RandomPresetsService _svc;
    private readonly SpawnableTypesService _spawnSvc;
    private readonly TypesService _types;
    private readonly Func<string, bool> _confirm;
    private readonly CeValidator _validator = new();
    private bool _suspendItemPersist;

    /// <param name="configPath">The resolved dzl config path.</param>
    /// <param name="confirm">Modal yes/no confirmation (returns true on Yes).</param>
    public RandomPresetsVm(string configPath, Func<string, bool> confirm)
        : this(new RandomPresetsService(configPath), new SpawnableTypesService(configPath),
               new TypesService(configPath), confirm) { }

    private RandomPresetsVm(RandomPresetsService svc, SpawnableTypesService spawnSvc, TypesService types,
                            Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.PresetsPath,
               "(no cfgrandompresets.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _spawnSvc = spawnSvc;
        _types = types;
        _confirm = confirm;
    }

    private List<RandomPreset>? _model;

    /// <summary>The parsed file, re-read lazily after every write/undo/redo/reload.</summary>
    private List<RandomPreset> Model => _model ??= _svc.Load();

    /// <inheritdoc/>
    protected override void InvalidateModelCache() => _model = null;

    /// <summary>Full reload from disk also drops the spawnable-types cache (the cross-file rule's referent).</summary>
    public override void Reload()
    {
        _spawnTypes = null;
        _typeNames = null;
        base.Reload();
    }

    /// <summary>Spawnable types, cached so the cross-file "unused preset" rule doesn't re-read the (possibly
    /// large) cfgspawnabletypes.xml on every keystroke. They only change on the other tab, so we refresh
    /// this on <see cref="Reload"/>, not per edit.</summary>
    private List<SpawnableType>? _spawnTypes;
    private List<SpawnableType> SpawnTypes => _spawnTypes ??= _spawnSvc.Load();

    /// <summary>Distinct classnames from every Types file of the mission — the suggestion pool for the
    /// add-item box (a preset item references an item classname). Cached; refreshed on <see cref="Reload"/>.
    /// Not a hard allowlist: preset items may legitimately reference classes outside types.xml, so free text
    /// stays valid — this only powers autocomplete.</summary>
    private List<string>? _typeNames;
    private List<string> TypeNames => _typeNames ??= _types.List()
        .Select(e => e.Name)
        .Where(n => !string.IsNullOrEmpty(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Up to 50 classnames matching the current add-item text (substring, case-insensitive),
    /// shown in the add-item combo's dropdown.</summary>
    public ObservableCollection<string> ItemSuggestions { get; } = new();

    /// <summary>Unfiltered backing store (all presets, sorted).</summary>
    private readonly List<PresetRowVm> _all = new();

    /// <summary>Live per-page validation findings for this file (chance ranges + unused presets).</summary>
    public ObservableCollection<CeFindingRow> Findings { get; } = new();

    /// <summary>The filtered master list shown in the list (both kinds), sorted by kind then name.</summary>
    public ObservableCollection<PresetRowVm> Presets { get; } = new();

    [ObservableProperty] private PresetRowVm? _selectedPreset;

    /// <summary>True when a preset is selected — gates the right-column "Edit preset" card.</summary>
    public bool HasSelection => SelectedPreset is not null;

    // "Edit preset" card (right column): live name + chance + kind of the selected preset.
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private double _editChanceValue;
    /// <summary>0 = cargo, 1 = attachments (binds the kind combo).</summary>
    [ObservableProperty] private int _editKindIndex;
    private bool _suspendCardSync;

    [ObservableProperty] private string _filter = "";

    /// <summary>Master-list state filter: 0 = all, 1 = enabled only, 2 = disabled (commented-out) only.</summary>
    [ObservableProperty] private int _stateFilterIndex;

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnStateFilterIndexChanged(int value) => ApplyFilter();

    /// <summary>Items of the currently selected preset (editable). Empty when none selected.</summary>
    public ObservableCollection<PresetItemVm> Items { get; } = new();

    // new-preset form
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private double _newPresetChanceValue = 0.1;
    /// <summary>true = Cargo, false = Attachments.</summary>
    [ObservableProperty] private bool _newPresetIsCargo = true;

    // new-item form
    [ObservableProperty] private string _newItemName = "";
    [ObservableProperty] private double _newItemChanceValue = 1.0;

    /// <summary>Set while a dropdown pick commits the name, so we don't re-filter (which would Clear the
    /// suggestion list and yank the just-selected item out from under the combo, blanking the field).</summary>
    public bool SuspendSuggestions { get; set; }

    partial void OnNewItemNameChanged(string value)
    {
        if (!SuspendSuggestions) RefreshItemSuggestions(value);
    }

    private void RefreshItemSuggestions(string text)
    {
        ItemSuggestions.Clear();
        var q = (text ?? "").Trim();
        if (q.Length == 0) return;
        foreach (var n in TypeNames
                     .Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase))
                     .Take(50))
            ItemSuggestions.Add(n);
    }

    /// <inheritdoc/>
    protected override void ReloadView() => LoadPresetsKeepingSelection();

    private void LoadPresetsKeepingSelection()
    {
        var prevKind = SelectedPreset?.Kind;
        var prevName = SelectedPreset?.Name;

        _all.Clear();
        foreach (var p in Model
                     .OrderBy(p => p.Kind)
                     .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            _all.Add(new PresetRowVm(p.Kind, p.Name, p.Chance, p.Items.Count, p.Disabled));
        }

        ApplyFilter();

        SelectedPreset = Presets.FirstOrDefault(r => r.Kind == prevKind && r.Name == prevName)
                         ?? Presets.FirstOrDefault();

        Validate();
    }

    /// <summary>Run the RandomPresets rules (chance ranges + unused presets) over the current model and the
    /// cached spawnable types, populate the <see cref="Findings"/> panel, and stamp each row's lint badge.
    /// Cheap enough to fire on every edit. Findings carry the preset name as their entry, so a row is "dirty"
    /// when a finding's entry matches its name.</summary>
    private void Validate()
    {
        var world = new CeWorld
        {
            RandomPresets = Model,
            SpawnableTypes = SpawnTypes,
            Files = new[] { new CeFileInfo(CeKind.RandomPresets, "cfgrandompresets.xml", FileLabel, HasFile) },
        };
        var findings = _validator.ValidateKind(world, CeKind.RandomPresets);

        Findings.Clear();
        foreach (var f in findings.OrderBy(f => f.Severity).ThenBy(f => f.EntryName, StringComparer.OrdinalIgnoreCase))
            Findings.Add(new CeFindingRow(f.Severity, f.Message, f.File, f.EntryName, f.Kind));

        var byEntry = findings.GroupBy(f => f.EntryName, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in _all)
        {
            if (byEntry.TryGetValue(row.Name, out var hits))
            {
                row.LintCount = hits.Count;
                row.LintTooltip = string.Join("\n", hits.Select(h => h.Message));
            }
            else
            {
                row.LintCount = 0;
                row.LintTooltip = "";
            }
        }
    }

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Presets.Clear();
        foreach (var r in _all)
        {
            if (f.Length > 0 && !r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;
            if (StateFilterIndex == 1 && r.IsDisabled) continue;   // enabled only
            if (StateFilterIndex == 2 && !r.IsDisabled) continue;  // disabled only
            Presets.Add(r);
        }
    }

    /// <summary>Select the preset named <paramref name="name"/> (e.g. from a validation-finding click),
    /// clearing the filter first if it would hide the row. No-op when no preset matches.</summary>
    public void SelectByEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!Presets.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            Filter = "";   // unfilter so the target row is visible (rebuilds Presets via ApplyFilter)
        SelectedPreset = Presets.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedPresetChanged(PresetRowVm? value)
    {
        // Sync the edit card to the new selection without echoing back as a rename/kind change.
        _suspendCardSync = true;
        EditName = value?.Name ?? "";
        EditChanceValue = value?.Chance ?? 0;
        EditKindIndex = value is { Kind: PresetKind.Attachments } ? 1 : 0;
        _suspendCardSync = false;
        OnPropertyChanged(nameof(HasSelection));
        LoadItemsForSelected();
    }

    partial void OnEditKindIndexChanged(int value)
    {
        if (!_suspendCardSync) ChangeSelectedKind(toCargo: value == 0);
    }

    /// <summary>Apply the edit-card name + chance to the selected preset in one undo step. Kind is applied
    /// live by the combo (<see cref="ChangeSelectedKind"/>), so it's not handled here.</summary>
    public void ApplyEdits()
    {
        if (SelectedPreset is not { } row) return;
        var newName = (EditName ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ name must not be empty"; return; }
        var chance = Math.Clamp(EditChanceValue, 0, 1);

        var renamed = !string.Equals(newName, row.Name, StringComparison.Ordinal);
        var chanceChanged = Math.Abs(chance - row.Chance) > 1e-9;
        if (!renamed && !chanceChanged) return;

        var kind = row.Kind;
        var name = row.Name;
        PushUndo();
        if (renamed)
        {
            if (!Report(_svc.RenamePreset(kind, name, newName))) return;
            name = newName;
        }
        if (chanceChanged) Report(_svc.SetPresetChance(kind, name, chance));

        LoadPresetsKeepingSelection();
        SelectedPreset = Presets.FirstOrDefault(r => r.Kind == kind &&
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Move the selected preset between cargo and attachments (from the edit-card kind combo).</summary>
    public void ChangeSelectedKind(bool toCargo)
    {
        if (SelectedPreset is not { } row) return;
        var to = toCargo ? PresetKind.Cargo : PresetKind.Attachments;
        if (row.Kind == to) return;
        PushUndo();
        if (Report(_svc.SetPresetKind(row.Kind, row.Name, to)))
        {
            var name = row.Name;
            LoadPresetsKeepingSelection();
            SelectedPreset = Presets.FirstOrDefault(r => r.Kind == to &&
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _suspendCardSync = true; // revert the combo to the unchanged kind
            EditKindIndex = row.Kind == PresetKind.Attachments ? 1 : 0;
            _suspendCardSync = false;
        }
    }

    private void LoadItemsForSelected()
    {
        _suspendItemPersist = true;
        try
        {
            foreach (var it in Items) it.Edited -= OnItemEdited;
            Items.Clear();
            if (SelectedPreset is { } row)
            {
                var preset = Model.FirstOrDefault(p => p.Kind == row.Kind &&
                    string.Equals(p.Name, row.Name, StringComparison.OrdinalIgnoreCase));
                if (preset is not null)
                    foreach (var i in preset.Items)
                    {
                        var ivm = new PresetItemVm(i.Name, i.Chance);
                        ivm.Edited += OnItemEdited;
                        Items.Add(ivm);
                    }
            }
        }
        finally { _suspendItemPersist = false; }
    }

    public void AddPreset()
    {
        var name = (NewPresetName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ preset name must not be empty"; return; }
        var chance = Math.Clamp(NewPresetChanceValue, 0, 1);
        var kind = NewPresetIsCargo ? PresetKind.Cargo : PresetKind.Attachments;

        PushUndo();
        if (Report(_svc.AddPreset(kind, name, chance)))
        {
            NewPresetName = "";
            LoadPresetsKeepingSelection();
            SelectedPreset = Presets.FirstOrDefault(r => r.Kind == kind && r.Name == name);
        }
    }

    public void RemoveSelectedPreset()
    {
        if (SelectedPreset is not { } row) { Status = "✗ select a preset to remove"; return; }
        if (!_confirm($"Remove the {row.Kind} preset \"{row.Name}\" and all its items?")) return;
        PushUndo();
        if (Report(_svc.RemovePreset(row.Kind, row.Name))) LoadPresetsKeepingSelection();
    }

    /// <summary>Toggle a preset between enabled and disabled (commented out in the file, not deleted), so an
    /// unused preset can be parked without losing it. Reloads to reflect the new state.</summary>
    public void ToggleDisabled(PresetRowVm? row)
    {
        if (row is null) { Status = "✗ select a preset to toggle"; return; }
        PushUndo();
        var result = row.IsDisabled
            ? _svc.EnablePreset(row.Kind, row.Name)
            : _svc.DisablePreset(row.Kind, row.Name);
        if (Report(result)) LoadPresetsKeepingSelection();
    }

    public void RenameSelectedPreset(string newName)
    {
        if (SelectedPreset is not { } row) { Status = "✗ select a preset to rename"; return; }
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ new name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.RenamePreset(row.Kind, row.Name, newName)))
        {
            LoadPresetsKeepingSelection();
            SelectedPreset = Presets.FirstOrDefault(r => r.Kind == row.Kind && r.Name == newName);
        }
    }

    public void AddItem()
    {
        if (SelectedPreset is not { } row) { Status = "✗ select a preset first"; return; }
        var name = (NewItemName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ item name must not be empty"; return; }
        var chance = Math.Clamp(NewItemChanceValue, 0, 1);

        PushUndo();
        if (Report(_svc.AddItem(row.Kind, row.Name, name, chance)))
        {
            NewItemName = "";
            LoadItemsForSelected();
            row.ItemCount = Items.Count;
        }
    }

    public void RemoveItem(PresetItemVm? item)
    {
        if (SelectedPreset is not { } row || item is null) { Status = "✗ select an item to remove"; return; }
        PushUndo();
        if (Report(_svc.RemoveItem(row.Kind, row.Name, item.Name)))
        {
            LoadItemsForSelected();
            row.ItemCount = Items.Count;
        }
    }

    /// <summary>An item's Name/Chance was edited inline — persist it (rename or chance change).</summary>
    private void OnItemEdited(PresetItemVm item)
    {
        if (_suspendItemPersist || SelectedPreset is not { } row) return;
        if (string.IsNullOrWhiteSpace(item.Name)) { Status = "✗ item name must not be empty"; return; }
        var chance = Math.Clamp(item.Chance, 0, 1);

        PushUndo();
        if (Report(_svc.SetItem(row.Kind, row.Name, item.OriginalName, chance, item.Name)))
        {
            item.CommitName();
            item.Chance = chance;
        }
        else
        {
            // revert raw text already untouched; reload to restore consistent view
            LoadItemsForSelected();
        }
    }
}

/// <summary>One master-list row: a cargo/attachments preset with its chance + item count.</summary>
public sealed partial class PresetRowVm : ObservableObject
{
    public PresetRowVm(PresetKind kind, string name, double chance, int itemCount, bool disabled = false)
    {
        Kind = kind;
        Name = name;
        _chance = chance;
        _chanceText = chance.ToString(CultureInfo.InvariantCulture);
        _itemCount = itemCount;
        _isDisabled = disabled;
    }

    public PresetKind Kind { get; }
    public string Name { get; }
    public string KindLabel => Kind == PresetKind.Cargo ? "cargo" : "attachments";

    /// <summary>True when this preset is commented out in the file (parked/inert). Drives the row's dimmed
    /// look and the enable/disable toggle's icon + tooltip.</summary>
    [ObservableProperty] private bool _isDisabled;

    partial void OnIsDisabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleTooltip));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    public string ToggleLabel => IsDisabled ? "On" : "Off";
    public string ToggleTooltip => IsDisabled ? "Enable (uncomment) this preset" : "Disable (comment out) this preset";

    [ObservableProperty] private double _chance;

    partial void OnChanceChanged(double value) => ChanceText = value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Editable text mirror of <see cref="Chance"/> (the chance box binds here, then the VM
    /// validates + persists on commit).</summary>
    [ObservableProperty] private string _chanceText;

    [ObservableProperty] private int _itemCount;

    // Per-row lint summary, refreshed by RandomPresetsVm.Validate after every load/edit.
    [ObservableProperty] private int _lintCount;
    [ObservableProperty] private string _lintTooltip = "";

    public bool HasLint => LintCount > 0;
    public string LintGlyph => LintCount == 0 ? "" : $"⚠ {LintCount}";

    partial void OnLintCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasLint));
        OnPropertyChanged(nameof(LintGlyph));
    }
}

/// <summary>One editable item row (Name + Chance) inside a preset.</summary>
public sealed partial class PresetItemVm : ObservableObject
{
    public PresetItemVm(string name, double chance)
    {
        _name = name;
        OriginalName = name;
        _chance = chance;
        _chanceText = chance.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The item name as last persisted (used to locate the element when renaming).</summary>
    public string OriginalName { get; private set; }

    /// <summary>Raised when Name or Chance is committed (LostFocus / Enter), so the VM persists.</summary>
    public event Action<PresetItemVm>? Edited;

    [ObservableProperty] private string _name;
    [ObservableProperty] private double _chance;

    /// <summary>Editable text mirror of <see cref="Chance"/>.</summary>
    [ObservableProperty] private string _chanceText;

    /// <summary>Called by the view when an editable field commits.</summary>
    public void Commit() => Edited?.Invoke(this);

    /// <summary>Adopt the current Name as the persisted baseline after a successful save.</summary>
    public void CommitName() => OriginalName = Name;
}
