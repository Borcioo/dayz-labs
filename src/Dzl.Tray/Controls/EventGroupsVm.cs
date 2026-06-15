using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="EventGroupsEditor"/> control (Events "Event Groups" tab): edits cfgeventgroups.xml —
/// named groups of objects an event spawns together. A filterable group list (master) drives the selected
/// group's children grid (detail: type + x/y/z/a + loot range + deloot). New children are added with default
/// offsets then tuned inline. Per-tab undo/redo + status from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class EventGroupsVm : RawXmlEditorVm
{
    private readonly EventGroupsService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspend;

    public EventGroupsVm(string configPath, Func<string, bool> confirm)
        : this(new EventGroupsService(configPath), confirm) { }

    private EventGroupsVm(EventGroupsService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.EventGroupsPath,
               "(no cfgeventgroups.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    private List<EventGroup>? _model;
    private List<EventGroup> Model => _model ??= _svc.Load();
    protected override void InvalidateModelCache() => _model = null;

    private readonly List<EventGroupRowVm> _allGroups = new();
    public ObservableCollection<EventGroupRowVm> Groups { get; } = new();
    [ObservableProperty] private EventGroupRowVm? _selectedGroup;
    [ObservableProperty] private string _filter = "";
    partial void OnFilterChanged(string value) => ApplyFilter();

    public ObservableCollection<EventGroupChildVm> Children { get; } = new();
    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _newChildType = "";

    protected override void ReloadView()
    {
        var prev = SelectedGroup?.Name;
        _allGroups.Clear();
        foreach (var g in Model.Where(g => !string.IsNullOrEmpty(g.Name)))
            _allGroups.Add(new EventGroupRowVm(g.Name, g.Children.Count));
        ApplyFilter();
        SelectedGroup = Groups.FirstOrDefault(g => string.Equals(g.Name, prev, StringComparison.OrdinalIgnoreCase))
                        ?? Groups.FirstOrDefault();
        LoadChildren();
    }

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Groups.Clear();
        foreach (var g in _allGroups)
            if (f.Length == 0 || g.Name.Contains(f, StringComparison.OrdinalIgnoreCase)) Groups.Add(g);
    }

    partial void OnSelectedGroupChanged(EventGroupRowVm? value) => LoadChildren();

    private void LoadChildren()
    {
        _suspend = true;
        try
        {
            foreach (var c in Children) c.Edited -= OnChildEdited;
            Children.Clear();
            if (SelectedGroup is { } g && Model.FirstOrDefault(x => string.Equals(x.Name, g.Name, StringComparison.OrdinalIgnoreCase)) is { } model)
                for (var i = 0; i < model.Children.Count; i++)
                {
                    var cvm = new EventGroupChildVm(i, model.Children[i]);
                    cvm.Edited += OnChildEdited;
                    Children.Add(cvm);
                }
        }
        finally { _suspend = false; }
    }

    protected override string? CaptureSelectionToken() => SelectedGroup?.Name;
    protected override void RestoreSelectionToken(string? token)
    {
        if (token is not null) SelectedGroup = Groups.FirstOrDefault(g => string.Equals(g.Name, token, StringComparison.OrdinalIgnoreCase)) ?? SelectedGroup;
    }

    private static bool TryD(string raw, out double v) => double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    private static bool TryI(string raw, out int v) => int.TryParse((raw ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    public void AddGroup()
    {
        var name = (NewGroupName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ group name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.AddGroup(name)))
        {
            NewGroupName = "";
            ReloadView();
            SelectedGroup = Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveSelectedGroup()
    {
        if (SelectedGroup is not { } g) { Status = "✗ select a group to remove"; return; }
        if (!_confirm($"Remove the group \"{g.Name}\" and all its children?")) return;
        PushUndo();
        if (Report(_svc.RemoveGroup(g.Name))) ReloadView();
    }

    /// <summary>Add a child with default offsets (tune x/y/z/a/loot inline afterwards).</summary>
    public void AddChild()
    {
        if (SelectedGroup is not { } g) { Status = "✗ select a group first"; return; }
        var type = (NewChildType ?? "").Trim();
        if (type.Length == 0) { Status = "✗ child type must not be empty"; return; }
        PushUndo();
        if (Report(_svc.AddChild(g.Name, type, 0, 0, 0, 0, lootMin: 0, lootMax: 1, deloot: false)))
        {
            NewChildType = "";
            LoadChildren();
            g.ChildCount = Children.Count;
        }
    }

    public void RemoveChild(EventGroupChildVm? child)
    {
        if (SelectedGroup is not { } g || child is null) { Status = "✗ select a child to remove"; return; }
        PushUndo();
        if (Report(_svc.RemoveChild(g.Name, child.Index)))
        {
            LoadChildren();
            g.ChildCount = Children.Count;
        }
    }

    private void OnChildEdited(EventGroupChildVm c)
    {
        if (_suspend || SelectedGroup is not { } g) return;
        var type = (c.Type ?? "").Trim();
        if (type.Length == 0) { Status = "✗ child type must not be empty"; LoadChildren(); return; }
        if (!TryD(c.XText, out var x) || !TryD(c.YText, out var y) || !TryD(c.ZText, out var z) || !TryD(c.AText, out var a))
        { Status = "✗ x/y/z/angle must be numbers"; LoadChildren(); return; }
        if (!TryI(c.LootMinText, out var lmin) || !TryI(c.LootMaxText, out var lmax))
        { Status = "✗ lootmin/lootmax must be whole numbers"; LoadChildren(); return; }
        PushUndo();
        if (!Report(_svc.SetChild(g.Name, c.Index, type, x, y, z, a, lmin, lmax, c.Deloot))) LoadChildren();
    }
}

/// <summary>One group row (name + child count) in the master list.</summary>
public sealed partial class EventGroupRowVm : ObservableObject
{
    public EventGroupRowVm(string name, int childCount) { Name = name; _childCount = childCount; }
    public string Name { get; }
    [ObservableProperty] private int _childCount;
}

/// <summary>One editable child object of a group (class + offset + loot range + deloot).</summary>
public sealed partial class EventGroupChildVm : ObservableObject
{
    public EventGroupChildVm(int index, EventGroupChild c)
    {
        Index = index;
        _type = c.Type;
        _xText = c.X.ToString(CultureInfo.InvariantCulture);
        _yText = c.Y.ToString(CultureInfo.InvariantCulture);
        _zText = c.Z.ToString(CultureInfo.InvariantCulture);
        _aText = c.A.ToString(CultureInfo.InvariantCulture);
        _lootMinText = c.LootMin.ToString(CultureInfo.InvariantCulture);
        _lootMaxText = c.LootMax.ToString(CultureInfo.InvariantCulture);
        _deloot = c.Deloot;
    }

    public int Index { get; }
    [ObservableProperty] private string _type;
    [ObservableProperty] private string _xText;
    [ObservableProperty] private string _yText;
    [ObservableProperty] private string _zText;
    [ObservableProperty] private string _aText;
    [ObservableProperty] private string _lootMinText;
    [ObservableProperty] private string _lootMaxText;
    [ObservableProperty] private bool _deloot;

    // The deloot checkbox commits immediately; text cells commit on edit-end via Commit().
    partial void OnDelootChanged(bool value) => Edited?.Invoke(this);

    public void Commit() => Edited?.Invoke(this);
    public event Action<EventGroupChildVm>? Edited;
}
