using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="PlayerSpawnsEditor"/> control (the Economy "Player Spawns" tab): edits the mission's
/// hierarchical <c>cfgplayerspawnpoints.xml</c>. A category nav-rail (<c>fresh</c>/<c>hop</c>/<c>travel</c> —
/// only those present) drives a dashboard detail: three friendly setting cards built from the documented
/// fields (spawn scoring, grid generation, group cycling — see the official DayZ Player Spawning Configuration),
/// any non-canonical keys preserved under "Other", and a groups↔positions master-detail.
/// All edits route through <see cref="PlayerSpawnsService"/> (never throws; snapshots a backup before each
/// write); per-tab undo/redo + the status line come from <see cref="RawXmlEditorVm"/>.
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
               "(no cfgplayerspawnpoints.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
        foreach (var f in AllFields) f.Edited += OnFieldEdited;
    }

    private List<SpawnCategory>? _model;
    private List<SpawnCategory> Model => _model ??= _svc.Load();
    protected override void InvalidateModelCache() => _model = null;

    // ── category nav-rail ────────────────────────────────────────────────
    public ObservableCollection<SpawnCategoryVm> CategoryTabs { get; } = new();

    /// <summary>The selected nav-rail tile; mirrors its name into <see cref="SelectedCategory"/>.</summary>
    [ObservableProperty] private SpawnCategoryVm? _selectedTab;

    /// <summary>Name of the active category (<c>fresh</c>/<c>hop</c>/<c>travel</c>). The single source of
    /// truth that drives the detail pane — set by the nav-rail (or directly).</summary>
    [ObservableProperty] private string? _selectedCategory;

    partial void OnSelectedTabChanged(SpawnCategoryVm? value)
    {
        if (value is not null && !string.Equals(value.Name, SelectedCategory, StringComparison.OrdinalIgnoreCase))
            SelectedCategory = value.Name;
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
        SelectedTab = CategoryTabs.FirstOrDefault(t => string.Equals(t.Name, value, StringComparison.OrdinalIgnoreCase));
        LoadCategoryDetail();
    }

    // ── friendly setting fields (stable instances; repopulated per category) ──
    // spawn_params — the runtime scoring distances (min/max metres). Higher distance reads as a better spawn.
    public SpawnFieldVm ScoreInfectedMin { get; } = new("spawn_params", "min_dist_infected");
    public SpawnFieldVm ScoreInfectedMax { get; } = new("spawn_params", "max_dist_infected");
    public SpawnFieldVm ScorePlayerMin { get; } = new("spawn_params", "min_dist_player");
    public SpawnFieldVm ScorePlayerMax { get; } = new("spawn_params", "max_dist_player");
    public SpawnFieldVm ScoreStaticMin { get; } = new("spawn_params", "min_dist_static");
    public SpawnFieldVm ScoreStaticMax { get; } = new("spawn_params", "max_dist_static");

    // generator_params — how candidate points are generated around each <pos>.
    public SpawnFieldVm GenDensity { get; } = new("generator_params", "grid_density");
    public SpawnFieldVm GenWidth { get; } = new("generator_params", "grid_width");
    public SpawnFieldVm GenHeight { get; } = new("generator_params", "grid_height");
    public SpawnFieldVm GenStaticMin { get; } = new("generator_params", "min_dist_static");
    public SpawnFieldVm GenStaticMax { get; } = new("generator_params", "max_dist_static");
    public SpawnFieldVm GenSteepMin { get; } = new("generator_params", "min_steepness");
    public SpawnFieldVm GenSteepMax { get; } = new("generator_params", "max_steepness");

    // group_params — group cycling.
    public SpawnFieldVm GrpEnable { get; } = new("group_params", "enablegroups", isBool: true);
    public SpawnFieldVm GrpAsRegular { get; } = new("group_params", "groups_as_regular", isBool: true);
    public SpawnFieldVm GrpLifetime { get; } = new("group_params", "lifetime");
    public SpawnFieldVm GrpCounter { get; } = new("group_params", "counter");

    private IEnumerable<SpawnFieldVm> AllFields => new[]
    {
        ScoreInfectedMin, ScoreInfectedMax, ScorePlayerMin, ScorePlayerMax, ScoreStaticMin, ScoreStaticMax,
        GenDensity, GenWidth, GenHeight, GenStaticMin, GenStaticMax, GenSteepMin, GenSteepMax,
        GrpEnable, GrpAsRegular, GrpLifetime, GrpCounter,
    };

    // Canonical keys per section — anything else in a bag is surfaced under "Other params" (never dropped).
    private static readonly HashSet<string> SpawnKeys = new(StringComparer.OrdinalIgnoreCase)
        { "min_dist_infected", "max_dist_infected", "min_dist_player", "max_dist_player", "min_dist_static", "max_dist_static" };
    private static readonly HashSet<string> GenKeys = new(StringComparer.OrdinalIgnoreCase)
        { "grid_density", "grid_width", "grid_height", "min_dist_static", "max_dist_static", "min_steepness", "max_steepness" };
    private static readonly HashSet<string> GroupKeys = new(StringComparer.OrdinalIgnoreCase)
        { "enablegroups", "groups_as_regular", "lifetime", "counter" };

    /// <summary>Non-canonical params present in the file (rare) — kept editable so a mission's custom keys are
    /// never silently lost. Each carries its section.</summary>
    public ObservableCollection<SpawnParamVm> OtherParams { get; } = new();
    public bool HasOtherParams => OtherParams.Count > 0;

    // ── spawn locations ──────────────────────────────────────────────────
    public ObservableCollection<SpawnGroupVm> Groups { get; } = new();
    [ObservableProperty] private SpawnGroupVm? _selectedGroup;
    public ObservableCollection<SpawnPosVm> Positions { get; } = new();

    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _newGroupContainer = "generator_posbubbles";

    // ── load ─────────────────────────────────────────────────────────────
    protected override void ReloadView()
    {
        var prev = SelectedCategory;
        CategoryTabs.Clear();
        foreach (var c in Model.Where(c => !string.IsNullOrEmpty(c.Name)))
        {
            var groups = c.Bubbles.SelectMany(b => b.Groups).ToList();
            CategoryTabs.Add(new SpawnCategoryVm(c.Name)
            {
                GroupCount = groups.Count,
                PosCount = groups.Sum(g => g.Positions.Count),
            });
        }

        SelectedCategory = CategoryTabs.Select(t => t.Name)
                               .FirstOrDefault(n => string.Equals(n, prev, StringComparison.OrdinalIgnoreCase))
                           ?? CategoryTabs.FirstOrDefault()?.Name;
        LoadCategoryDetail();   // force even when the name reference is unchanged
    }

    private SpawnCategory? FindCategory(string? name) =>
        name is null ? null : Model.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private void LoadCategoryDetail()
    {
        _suspendPersist = true;
        try
        {
            DetachOther(); OtherParams.Clear();
            Groups.Clear();
            var cat = FindCategory(SelectedCategory);

            LoadField(ScoreInfectedMin, cat?.SpawnParams); LoadField(ScoreInfectedMax, cat?.SpawnParams);
            LoadField(ScorePlayerMin, cat?.SpawnParams); LoadField(ScorePlayerMax, cat?.SpawnParams);
            LoadField(ScoreStaticMin, cat?.SpawnParams); LoadField(ScoreStaticMax, cat?.SpawnParams);
            LoadField(GenDensity, cat?.GeneratorParams); LoadField(GenWidth, cat?.GeneratorParams);
            LoadField(GenHeight, cat?.GeneratorParams); LoadField(GenStaticMin, cat?.GeneratorParams);
            LoadField(GenStaticMax, cat?.GeneratorParams); LoadField(GenSteepMin, cat?.GeneratorParams);
            LoadField(GenSteepMax, cat?.GeneratorParams);
            LoadField(GrpEnable, cat?.GroupParams); LoadField(GrpAsRegular, cat?.GroupParams);
            LoadField(GrpLifetime, cat?.GroupParams); LoadField(GrpCounter, cat?.GroupParams);

            if (cat is not null)
            {
                FillOther("spawn_params", cat.SpawnParams, SpawnKeys);
                FillOther("generator_params", cat.GeneratorParams, GenKeys);
                FillOther("group_params", cat.GroupParams, GroupKeys);

                foreach (var b in cat.Bubbles)
                    foreach (var g in b.Groups)
                        if (!string.IsNullOrEmpty(g.Name))
                            Groups.Add(new SpawnGroupVm(b.Container, g.Name, g.Positions.Count));
            }
        }
        finally { _suspendPersist = false; }

        OnPropertyChanged(nameof(HasOtherParams));
        SelectedGroup = Groups.FirstOrDefault();
        LoadPositionsForSelected();
    }

    private static void LoadField(SpawnFieldVm f, IReadOnlyList<SpawnParam>? bag)
    {
        var hit = bag?.FirstOrDefault(p => string.Equals(p.Name, f.Key, StringComparison.OrdinalIgnoreCase));
        if (f.IsBool) f.Flag = hit is not null && IsTrue(hit.Value);
        else f.Number = hit is not null && TryDouble(hit.Value, out var d) ? d : null;
    }

    private void FillOther(string section, IReadOnlyList<SpawnParam> bag, HashSet<string> canonical)
    {
        foreach (var p in bag.Where(p => !canonical.Contains(p.Name)))
        {
            var vm = new SpawnParamVm(section, p.Name, p.Value);
            vm.Edited += OnOtherParamEdited;
            OtherParams.Add(vm);
        }
    }

    private void DetachOther()
    {
        foreach (var p in OtherParams) p.Edited -= OnOtherParamEdited;
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
                    for (var i = 0; i < model.Positions.Count; i++)
                    {
                        var pvm = new SpawnPosVm(i, model.Positions[i].X, model.Positions[i].Z);
                        pvm.Edited += OnPosEdited;
                        Positions.Add(pvm);
                    }
            }
        }
        finally { _suspendPersist = false; }
    }

    // Keep the selected category across undo/redo (the base re-selects by token after a restore).
    protected override string? CaptureSelectionToken() => SelectedCategory;
    protected override void RestoreSelectionToken(string? token)
    {
        if (token is not null && CategoryTabs.Any(t => string.Equals(t.Name, token, StringComparison.OrdinalIgnoreCase)))
            SelectedCategory = token;
    }

    private static bool TryDouble(string raw, out double value) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool IsTrue(string raw) =>
        string.Equals((raw ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase) || raw?.Trim() == "1";

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    // ── persistence ──────────────────────────────────────────────────────
    private void OnFieldEdited(SpawnFieldVm f)
    {
        if (_suspendPersist || SelectedCategory is not { } cat) return;
        string value;
        if (f.IsBool) value = f.Flag ? "true" : "false";
        else if (f.Number is { } n) value = Format(n);
        else return;   // a cleared numeric field leaves the file untouched (no way to express "remove")
        PushUndo();
        Report(_svc.SetParam(cat, f.Section, f.Key, value));
    }

    private void OnOtherParamEdited(SpawnParamVm param)
    {
        if (_suspendPersist || SelectedCategory is not { } cat) return;
        var newName = (param.Name ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ param name must not be empty"; return; }
        if (!TryDouble(param.Value, out _)) { Status = "✗ value must be a number"; LoadCategoryDetail(); return; }
        PushUndo();
        // The key IS the element name, so a rename must move the element (not upsert under the new name and
        // orphan the old). Route a rename first, then set the value — same as Globals.
        if (!string.Equals(newName, param.OriginalName, StringComparison.Ordinal) && !string.IsNullOrEmpty(param.OriginalName))
            if (!Report(_svc.RenameParam(cat, param.Section, param.OriginalName, newName))) { LoadCategoryDetail(); return; }
        if (Report(_svc.SetParam(cat, param.Section, newName, param.Value ?? ""))) param.CommitName();
    }

    /// <summary>Add a non-canonical param (key + numeric value) to a section of the selected category.</summary>
    public void AddOtherParam(string section, string name, string value)
    {
        if (SelectedCategory is not { } cat) { Status = "✗ select a category first"; return; }
        name = (name ?? "").Trim();
        if (name.Length == 0) { Status = "✗ param name must not be empty"; return; }
        if (OtherParams.Any(p => string.Equals(p.Section, section, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        { Status = $"✗ param \"{name}\" already exists in {section}"; return; }
        if (!TryDouble(value, out _)) { Status = "✗ value must be a number"; return; }
        PushUndo();
        if (Report(_svc.SetParam(cat, section, name, value ?? ""))) LoadCategoryDetail();
    }

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

/// <summary>One nav-rail tile: a spawn category (<c>fresh</c>/<c>hop</c>/<c>travel</c>) with a friendly title,
/// subtitle and live counts. <c>fresh</c> is required; <c>hop</c>/<c>travel</c> apply only to official servers
/// (per the DayZ Player Spawning Configuration docs) and are flagged accordingly.</summary>
public sealed partial class SpawnCategoryVm : ObservableObject
{
    public SpawnCategoryVm(string name)
    {
        Name = name;
        (Display, Subtitle, IsOfficial) = name.ToLowerInvariant() switch
        {
            "fresh" => ("Fresh", "new characters · required", false),
            "hop" => ("Hop", "same-map hop · official", true),
            "travel" => ("Travel", "cross-map travel · official", true),
            _ => (char.ToUpperInvariant(name.FirstOrDefault()) + name[(name.Length > 0 ? 1 : 0)..], "", false),
        };
    }

    public string Name { get; }
    public string Display { get; }
    public string Subtitle { get; }
    public bool IsOfficial { get; }

    [ObservableProperty] private int _groupCount;
    [ObservableProperty] private int _posCount;
}

/// <summary>One documented setting field. Numeric fields bind <see cref="Number"/> (nullable = absent in the
/// file); boolean fields (<c>enablegroups</c>, <c>groups_as_regular</c>) bind <see cref="Flag"/>. <see cref="Section"/>
/// + <see cref="Key"/> locate the element for persistence.</summary>
public sealed partial class SpawnFieldVm : ObservableObject
{
    public SpawnFieldVm(string section, string key, bool isBool = false)
    {
        Section = section;
        Key = key;
        IsBool = isBool;
    }

    public string Section { get; }
    public string Key { get; }
    public bool IsBool { get; }

    public event Action<SpawnFieldVm>? Edited;

    [ObservableProperty] private double? _number;
    [ObservableProperty] private bool _flag;

    partial void OnNumberChanged(double? value) { if (!IsBool) Edited?.Invoke(this); }
    partial void OnFlagChanged(bool value) { if (IsBool) Edited?.Invoke(this); }
}

/// <summary>One editable non-canonical param row (Name + Value) under "Other". <see cref="Section"/> names the
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
    public string OriginalName { get; private set; }
    public event Action<SpawnParamVm>? Edited;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _value;

    public void Commit() => Edited?.Invoke(this);
    public void CommitName() => OriginalName = Name;
}

/// <summary>One groups-list row: a named position group, the bubbles container it lives in, and its count.</summary>
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
