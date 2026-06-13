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
    private readonly Func<string, bool> _confirm;
    private readonly CeValidator _validator = new();
    private bool _suspendItemPersist;

    /// <param name="configPath">The resolved dzl config path.</param>
    /// <param name="confirm">Modal yes/no confirmation (returns true on Yes).</param>
    public RandomPresetsVm(string configPath, Func<string, bool> confirm)
        : this(new RandomPresetsService(configPath), new SpawnableTypesService(configPath), confirm) { }

    private RandomPresetsVm(RandomPresetsService svc, SpawnableTypesService spawnSvc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.PresetsPath,
               "(no cfgrandompresets.xml — pick/scaffold a server mission)")
    {
        _svc = svc;
        _spawnSvc = spawnSvc;
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
        base.Reload();
    }

    /// <summary>Spawnable types, cached so the cross-file "unused preset" rule doesn't re-read the (possibly
    /// large) cfgspawnabletypes.xml on every keystroke. They only change on the other tab, so we refresh
    /// this on <see cref="Reload"/>, not per edit.</summary>
    private List<SpawnableType>? _spawnTypes;
    private List<SpawnableType> SpawnTypes => _spawnTypes ??= _spawnSvc.Load();

    /// <summary>Unfiltered backing store (all presets, sorted).</summary>
    private readonly List<PresetRowVm> _all = new();

    /// <summary>Live per-page validation findings for this file (chance ranges + unused presets).</summary>
    public ObservableCollection<CeFindingRow> Findings { get; } = new();

    /// <summary>The filtered master list shown in the list (both kinds), sorted by kind then name.</summary>
    public ObservableCollection<PresetRowVm> Presets { get; } = new();

    [ObservableProperty] private PresetRowVm? _selectedPreset;

    [ObservableProperty] private string _filter = "";

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Items of the currently selected preset (editable). Empty when none selected.</summary>
    public ObservableCollection<PresetItemVm> Items { get; } = new();

    // new-preset form
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _newPresetChance = "0.1";
    /// <summary>true = Cargo, false = Attachments.</summary>
    [ObservableProperty] private bool _newPresetIsCargo = true;

    // new-item form
    [ObservableProperty] private string _newItemName = "";
    [ObservableProperty] private string _newItemChance = "1.0";

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
            _all.Add(new PresetRowVm(p.Kind, p.Name, p.Chance, p.Items.Count));
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
            if (f.Length == 0 || r.Name.Contains(f, StringComparison.OrdinalIgnoreCase))
                Presets.Add(r);
    }

    partial void OnSelectedPresetChanged(PresetRowVm? value) => LoadItemsForSelected();

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
        if (!TryChance(NewPresetChance, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }
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

    /// <summary>Persist the selected preset's edited chance (called when its chance box commits).</summary>
    public void SaveSelectedPresetChance()
    {
        if (SelectedPreset is not { } row) return;
        if (!TryChance(row.ChanceText, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }
        PushUndo();
        if (Report(_svc.SetPresetChance(row.Kind, row.Name, chance))) row.Chance = chance;
    }

    public void AddItem()
    {
        if (SelectedPreset is not { } row) { Status = "✗ select a preset first"; return; }
        var name = (NewItemName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ item name must not be empty"; return; }
        if (!TryChance(NewItemChance, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }

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
        if (!TryChance(item.ChanceText, out var chance)) { Status = "✗ chance must be a number 0..1"; return; }

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
    public PresetRowVm(PresetKind kind, string name, double chance, int itemCount)
    {
        Kind = kind;
        Name = name;
        _chance = chance;
        _chanceText = chance.ToString(CultureInfo.InvariantCulture);
        _itemCount = itemCount;
    }

    public PresetKind Kind { get; }
    public string Name { get; }
    public string KindLabel => Kind == PresetKind.Cargo ? "cargo" : "attachments";

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
