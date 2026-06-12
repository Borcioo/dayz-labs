using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="PlayerSpawnsEditor"/> control (the Economy "Player Spawns" tab): edits the mission's
/// hierarchical <c>cfgplayerspawnpoints.xml</c>. A category selector (fresh/hop/travel — only those present)
/// drives three editable param sections (spawn_params / generator_params / group_params, each a flat Name/Value
/// bag) and a groups list (across all bubbles containers); selecting a group shows its positions grid (X, Z).
/// All edits route through <see cref="PlayerSpawnsService"/> (never throws; snapshots a backup before each
/// write). Per-tab undo/redo + the status line come from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class PlayerSpawnsVm : RawXmlEditorVm
{
    private readonly PlayerSpawnsService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspendPersist;

    public PlayerSpawnsVm(string configPath, Func<string, bool> confirm)
        : this(new PlayerSpawnsService(configPath), confirm) { }

    private PlayerSpawnsVm(PlayerSpawnsService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.SpawnsPath,
               "(no cfgplayerspawnpoints.xml — pick/scaffold a server mission)")
    {
        _svc = svc;
        _confirm = confirm;
    }

    // ------------------------------------------------------------------
    // Parsed model (cached between writes — selection clicks reuse it)
    // ------------------------------------------------------------------

    private List<SpawnCategory>? _model;

    /// <summary>The parsed file, re-read lazily after every write/undo/redo/reload.</summary>
    private List<SpawnCategory> Model => _model ??= _svc.Load();

    /// <inheritdoc/>
    protected override void InvalidateModelCache() => _model = null;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    /// <summary>Names of the categories present in the file (fresh/hop/travel/…), in file order.</summary>
    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty] private string? _selectedCategory;

    /// <summary>spawn_params of the selected category (editable Name/Value rows).</summary>
    public ObservableCollection<SpawnParamVm> SpawnParams { get; } = new();

    /// <summary>generator_params of the selected category.</summary>
    public ObservableCollection<SpawnParamVm> GeneratorParams { get; } = new();

    /// <summary>group_params of the selected category.</summary>
    public ObservableCollection<SpawnParamVm> GroupParams { get; } = new();

    /// <summary>Groups across all bubbles containers of the selected category.</summary>
    public ObservableCollection<SpawnGroupVm> Groups { get; } = new();

    [ObservableProperty] private SpawnGroupVm? _selectedGroup;

    /// <summary>Positions of the selected group (editable X/Z rows).</summary>
    public ObservableCollection<SpawnPosVm> Positions { get; } = new();

    // add-group form
    [ObservableProperty] private string _newGroupName = "";

    /// <summary>The container new groups are added to (defaults to generator_posbubbles).</summary>
    [ObservableProperty] private string _newGroupContainer = "generator_posbubbles";

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void ReloadView() => LoadCategoriesKeepingSelection();

    private void LoadCategoriesKeepingSelection()
    {
        var prev = SelectedCategory;
        Categories.Clear();
        foreach (var c in Model)
            if (!string.IsNullOrEmpty(c.Name)) Categories.Add(c.Name);

        SelectedCategory = Categories.FirstOrDefault(c => string.Equals(c, prev, StringComparison.OrdinalIgnoreCase))
                           ?? Categories.FirstOrDefault();
        // If selection didn't change reference, force a detail reload anyway.
        LoadCategoryDetail();
    }

    partial void OnSelectedCategoryChanged(string? value) => LoadCategoryDetail();

    private SpawnCategory? FindCategory(string? name) =>
        name is null ? null
        : Model.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private void LoadCategoryDetail()
    {
        _suspendPersist = true;
        try
        {
            DetachParams(SpawnParams); SpawnParams.Clear();
            DetachParams(GeneratorParams); GeneratorParams.Clear();
            DetachParams(GroupParams); GroupParams.Clear();
            Groups.Clear();

            if (FindCategory(SelectedCategory) is { } cat)
            {
                FillParams(SpawnParams, "spawn_params", cat.SpawnParams);
                FillParams(GeneratorParams, "generator_params", cat.GeneratorParams);
                FillParams(GroupParams, "group_params", cat.GroupParams);

                foreach (var b in cat.Bubbles)
                    foreach (var g in b.Groups)
                        if (!string.IsNullOrEmpty(g.Name))
                            Groups.Add(new SpawnGroupVm(b.Container, g.Name, g.Positions.Count));
            }
        }
        finally { _suspendPersist = false; }

        SelectedGroup = Groups.FirstOrDefault();
        LoadPositionsForSelected();
    }

    private void FillParams(ObservableCollection<SpawnParamVm> dest, string section, IReadOnlyList<SpawnParam> src)
    {
        foreach (var p in src)
        {
            var vm = new SpawnParamVm(section, p.Name, p.Value);
            vm.Edited += OnParamEdited;
            dest.Add(vm);
        }
    }

    private void DetachParams(ObservableCollection<SpawnParamVm> rows)
    {
        foreach (var r in rows) r.Edited -= OnParamEdited;
    }

    partial void OnSelectedGroupChanged(SpawnGroupVm? value) => LoadPositionsForSelected();

    private void LoadPositionsForSelected()
    {
        _suspendPersist = true;
        try
        {
            foreach (var p in Positions) p.Edited -= OnPosEdited;
            Positions.Clear();

            if (SelectedGroup is { } grp && FindCategory(SelectedCategory) is { } cat)
            {
                var bubbles = cat.Bubbles.FirstOrDefault(b =>
                    string.Equals(b.Container, grp.Container, StringComparison.OrdinalIgnoreCase));
                var model = bubbles?.Groups.FirstOrDefault(g =>
                    string.Equals(g.Name, grp.Name, StringComparison.OrdinalIgnoreCase));
                if (model is not null)
                {
                    for (var i = 0; i < model.Positions.Count; i++)
                    {
                        var pvm = new SpawnPosVm(i, model.Positions[i].X, model.Positions[i].Z);
                        pvm.Edited += OnPosEdited;
                        Positions.Add(pvm);
                    }
                }
            }
        }
        finally { _suspendPersist = false; }
    }

    // ------------------------------------------------------------------
    // Double parse (invariant culture)
    // ------------------------------------------------------------------

    private static bool TryDouble(string raw, out double value) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ------------------------------------------------------------------
    // Param edits
    // ------------------------------------------------------------------

    private void OnParamEdited(SpawnParamVm param)
    {
        if (_suspendPersist || SelectedCategory is not { } cat) return;
        if (string.IsNullOrWhiteSpace(param.Name)) { Status = "✗ param name must not be empty"; return; }
        PushUndo();
        Report(_svc.SetParam(cat, param.Section, param.Name, param.Value ?? ""));
    }

    /// <summary>Add a new param row (key + value) to a section of the selected category and persist it.</summary>
    public void AddParam(string section, string name, string value)
    {
        if (SelectedCategory is not { } cat) { Status = "✗ select a category first"; return; }
        name = (name ?? "").Trim();
        if (name.Length == 0) { Status = "✗ param name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.SetParam(cat, section, name, value ?? ""))) LoadCategoryDetail();
    }

    // ------------------------------------------------------------------
    // Group edits
    // ------------------------------------------------------------------

    public void AddGroup()
    {
        if (SelectedCategory is not { } cat) { Status = "✗ select a category first"; return; }
        var name = (NewGroupName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ group name must not be empty"; return; }
        var container = string.IsNullOrWhiteSpace(NewGroupContainer) ? "generator_posbubbles" : NewGroupContainer.Trim();
        PushUndo();
        if (Report(_svc.AddGroup(cat, container, name)))
        {
            NewGroupName = "";
            LoadCategoryDetail();
            SelectedGroup = Groups.FirstOrDefault(g =>
                string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.Container, container, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveSelectedGroup()
    {
        if (SelectedCategory is not { } cat || SelectedGroup is not { } grp)
        { Status = "✗ select a group to remove"; return; }
        if (!_confirm($"Remove the group \"{grp.Name}\" and all its positions?")) return;
        PushUndo();
        if (Report(_svc.RemoveGroup(cat, grp.Container, grp.Name))) LoadCategoryDetail();
    }

    public void RenameSelectedGroup(string newName)
    {
        if (SelectedCategory is not { } cat || SelectedGroup is not { } grp)
        { Status = "✗ select a group to rename"; return; }
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ new name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.RenameGroup(cat, grp.Container, grp.Name, newName)))
        {
            var container = grp.Container;
            LoadCategoryDetail();
            SelectedGroup = Groups.FirstOrDefault(g =>
                string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.Container, container, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ------------------------------------------------------------------
    // Position edits
    // ------------------------------------------------------------------

    public void AddPos(string xText, string zText)
    {
        if (SelectedCategory is not { } cat || SelectedGroup is not { } grp)
        { Status = "✗ select a group first"; return; }
        if (!TryDouble(xText, out var x)) { Status = "✗ X must be a number"; return; }
        if (!TryDouble(zText, out var z)) { Status = "✗ Z must be a number"; return; }
        PushUndo();
        if (Report(_svc.AddPos(cat, grp.Container, grp.Name, x, z)))
        {
            LoadPositionsForSelected();
            grp.PosCount = Positions.Count;
        }
    }

    public void RemovePos(SpawnPosVm? pos)
    {
        if (SelectedCategory is not { } cat || SelectedGroup is not { } grp || pos is null)
        { Status = "✗ select a position to remove"; return; }
        PushUndo();
        if (Report(_svc.RemovePos(cat, grp.Container, grp.Name, pos.Index)))
        {
            LoadPositionsForSelected();
            grp.PosCount = Positions.Count;
        }
    }

    private void OnPosEdited(SpawnPosVm pos)
    {
        if (_suspendPersist || SelectedCategory is not { } cat || SelectedGroup is not { } grp) return;
        if (!TryDouble(pos.XText, out var x)) { Status = "✗ X must be a number"; return; }
        if (!TryDouble(pos.ZText, out var z)) { Status = "✗ Z must be a number"; return; }
        PushUndo();
        if (!Report(_svc.SetPos(cat, grp.Container, grp.Name, pos.Index, x, z))) LoadPositionsForSelected();
    }
}

/// <summary>One editable param row (Name + Value) inside a param section. <see cref="Section"/> names which
/// bag it belongs to (spawn_params|generator_params|group_params).</summary>
public sealed partial class SpawnParamVm : ObservableObject
{
    public SpawnParamVm(string section, string name, string value)
    {
        Section = section;
        _name = name;
        OriginalName = name;
        _value = value;
    }

    public string Section { get; }

    /// <summary>The param name as last persisted (the section is keyed by name; upsert uses the current Name).</summary>
    public string OriginalName { get; private set; }

    public event Action<SpawnParamVm>? Edited;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _value;

    public void Commit() { OriginalName = Name; Edited?.Invoke(this); }
}

/// <summary>One groups-list row: a named position group plus the bubbles container it lives in and its count.</summary>
public sealed partial class SpawnGroupVm : ObservableObject
{
    public SpawnGroupVm(string container, string name, int posCount)
    {
        Container = container;
        Name = name;
        _posCount = posCount;
    }

    public string Container { get; }
    public string Name { get; }

    [ObservableProperty] private int _posCount;
}

/// <summary>One editable position row (X + Z) inside a group. <see cref="Index"/> addresses it for edits.</summary>
public sealed partial class SpawnPosVm : ObservableObject
{
    public SpawnPosVm(int index, double x, double z)
    {
        Index = index;
        _xText = x.ToString(CultureInfo.InvariantCulture);
        _zText = z.ToString(CultureInfo.InvariantCulture);
    }

    public int Index { get; }

    [ObservableProperty] private string _xText;
    [ObservableProperty] private string _zText;

    public void Commit() => Edited?.Invoke(this);
    public event Action<SpawnPosVm>? Edited;
}
