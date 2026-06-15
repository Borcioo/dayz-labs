using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="EventsEditor"/> control (the Economy "Events" tab): a master list of CE events
/// from <c>db/events.xml</c> plus a detail pane for the selected event (scalar fields, flags, position,
/// limit, active, and a children grid). All edits route through <see cref="EventsService"/> (never throws;
/// snapshots a backup before each write). Per-tab undo/redo + the status line come from
/// <see cref="RawXmlEditorVm"/>; detail fields persist per-commit (guarded by <c>_suspendDetailPersist</c>),
/// which stays here by design.
/// </summary>
public sealed partial class EventsVm : RawXmlEditorVm
{
    private readonly EventsService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspendDetailPersist;

    /// <param name="configPath">The resolved dzl config path.</param>
    /// <param name="confirm">Modal yes/no confirmation (returns true on Yes).</param>
    public EventsVm(string configPath, Func<string, bool> confirm)
        : this(new EventsService(configPath), confirm) { }

    private EventsVm(EventsService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.EventsPath,
               "(no events.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    private List<CeEvent>? _model;

    /// <summary>The parsed file, re-read lazily after every write/undo/redo/reload.</summary>
    private List<CeEvent> Model => _model ??= _svc.Load();

    /// <inheritdoc/>
    protected override void InvalidateModelCache() => _model = null;

    /// <summary>Unfiltered backing store.</summary>
    private readonly List<EventRowVm> _all = new();

    /// <summary>The filtered master list shown in the DataGrid.</summary>
    public ObservableCollection<EventRowVm> Events { get; } = new();

    [ObservableProperty] private EventRowVm? _selectedEvent;

    /// <summary>True when an event is selected — gates the detail pane's inline-rename box + button.</summary>
    public bool HasSelection => SelectedEvent is not null;

    /// <summary>Editable name of the selected event (the detail pane's inline-rename box binds here; Enter or
    /// the Rename button commits via <see cref="CommitRename"/>). Synced to the selection, not persisted on
    /// keystroke.</summary>
    [ObservableProperty] private string _renameText = "";

    [ObservableProperty] private string _filter = "";

    // new-event form
    [ObservableProperty] private string _newEventName = "";

    // new-child form
    [ObservableProperty] private string _newChildType = "";
    [ObservableProperty] private string _newChildMin = "0";
    [ObservableProperty] private string _newChildMax = "1";
    [ObservableProperty] private string _newChildLootMin = "0";
    [ObservableProperty] private string _newChildLootMax = "0";

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Children of the currently selected event (editable rows in the children grid).</summary>
    public ObservableCollection<EventChildRowVm> Children { get; } = new();

    // Scalars (detail pane)
    [ObservableProperty] private string _detailNominal = "0";
    [ObservableProperty] private string _detailMin = "0";
    [ObservableProperty] private string _detailMax = "0";
    [ObservableProperty] private string _detailLifetime = "0";
    [ObservableProperty] private string _detailRestock = "0";
    [ObservableProperty] private string _detailSafeRadius = "0";
    [ObservableProperty] private string _detailDistanceRadius = "0";
    [ObservableProperty] private string _detailCleanupRadius = "0";

    // Flags
    [ObservableProperty] private bool _detailDeletable;
    [ObservableProperty] private bool _detailInitRandom;
    [ObservableProperty] private bool _detailRemoveDamaged;

    // Strings
    [ObservableProperty] private string _detailPosition = "";
    [ObservableProperty] private string _detailLimit = "";
    [ObservableProperty] private bool _detailActive;

    /// <inheritdoc/>
    protected override void ReloadView() => LoadKeepingSelection();

    private void LoadKeepingSelection()
    {
        var prevName = SelectedEvent?.Name;

        _all.Clear();
        foreach (var ev in Model)
            _all.Add(new EventRowVm(ev.Name, ev.Nominal, ev.Min, ev.Max, ev.Lifetime, ev.Active, ev.Children.Count,
                ev.Deletable, ev.InitRandom, ev.RemoveDamaged));

        ApplyFilter();

        SelectedEvent = Events.FirstOrDefault(r => string.Equals(r.Name, prevName, StringComparison.OrdinalIgnoreCase))
                        ?? Events.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Events.Clear();
        foreach (var r in _all)
        {
            if (f.Length == 0 || r.Name.Contains(f, StringComparison.OrdinalIgnoreCase))
                Events.Add(r);
        }
    }

    /// <summary>Select the event named <paramref name="name"/> (e.g. from a dashboard finding click), clearing
    /// the filter only if it would hide the row. Selects the entry directly — does NOT filter the list.</summary>
    public void SelectByEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!Events.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            Filter = "";
        SelectedEvent = Events.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedEventChanged(EventRowVm? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        LoadDetailForSelected();
    }

    private void LoadDetailForSelected()
    {
        _suspendDetailPersist = true;
        try
        {
            Children.Clear();
            if (SelectedEvent is not { } row)
            {
                ClearDetail();
                return;
            }

            var ev = Model
                .FirstOrDefault(e => string.Equals(e.Name, row.Name, StringComparison.OrdinalIgnoreCase));
            if (ev is null) { ClearDetail(); return; }

            RenameText           = ev.Name;
            DetailNominal        = ev.Nominal.ToString();
            DetailMin            = ev.Min.ToString();
            DetailMax            = ev.Max.ToString();
            DetailLifetime       = ev.Lifetime.ToString();
            DetailRestock        = ev.Restock.ToString();
            DetailSafeRadius     = ev.SafeRadius.ToString();
            DetailDistanceRadius = ev.DistanceRadius.ToString();
            DetailCleanupRadius  = ev.CleanupRadius.ToString();
            DetailDeletable      = ev.Deletable;
            DetailInitRandom     = ev.InitRandom;
            DetailRemoveDamaged  = ev.RemoveDamaged;
            DetailPosition       = ev.Position;
            DetailLimit          = ev.Limit;
            DetailActive         = ev.Active;

            foreach (var c in ev.Children)
                Children.Add(new EventChildRowVm(c.Type, c.Min, c.Max, c.LootMin, c.LootMax));
        }
        finally { _suspendDetailPersist = false; }
    }

    private void ClearDetail()
    {
        RenameText = "";
        DetailNominal = DetailMin = DetailMax = DetailLifetime = DetailRestock =
            DetailSafeRadius = DetailDistanceRadius = DetailCleanupRadius = "0";
        DetailDeletable = DetailInitRandom = DetailRemoveDamaged = false;
        DetailPosition = DetailLimit = "";
        DetailActive = false;
    }

    public void AddEvent()
    {
        var name = (NewEventName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ event name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.AddEvent(name)))
        {
            NewEventName = "";
            LoadKeepingSelection();
            SelectedEvent = Events.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveSelectedEvent()
    {
        if (SelectedEvent is not { } row) { Status = "✗ select an event to remove"; return; }
        if (!_confirm($"Remove the event \"{row.Name}\" and all its children?")) return;
        PushUndo();
        if (Report(_svc.RemoveEvent(row.Name))) LoadKeepingSelection();
    }

    public void RenameSelectedEvent(string newName)
    {
        if (SelectedEvent is not { } row) { Status = "✗ select an event to rename"; return; }
        newName = (newName ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ new name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.RenameEvent(row.Name, newName)))
        {
            LoadKeepingSelection();
            SelectedEvent = Events.FirstOrDefault(r => string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Commit the detail pane's inline-rename box: rename the selected event to <see cref="RenameText"/>.
    /// No-op when nothing changed; the empty-name guard lives in <see cref="RenameSelectedEvent"/>.</summary>
    public void CommitRename()
    {
        if (SelectedEvent is not { } row) { Status = "✗ select an event to rename"; return; }
        var newName = (RenameText ?? "").Trim();
        if (string.Equals(newName, row.Name, StringComparison.Ordinal)) return;
        RenameSelectedEvent(newName);
    }

    private static bool TryInt(string? raw, out int value) =>
        int.TryParse((raw ?? "").Trim(), out value) && value >= 0;

    /// <summary>Persist a scalar field from the detail pane. Field names: nominal|min|max|lifetime|restock|
    /// saferadius|distanceradius|cleanupradius.</summary>
    public void SaveScalar(string field, string rawValue)
    {
        if (_suspendDetailPersist || SelectedEvent is not { } row) return;
        if (!TryInt(rawValue, out var value)) { Status = $"✗ {field} must be a non-negative integer"; return; }
        PushUndo();
        if (Report(_svc.SetScalar(row.Name, field, value)))
        {
            // Update the master-row summary column too (for nominal, active).
            RefreshMasterRow(row.Name);
            WarnIfMinGtMax();
        }
    }

    // Non-blocking: the Core lint flags min>max later; surface it live so the user isn't surprised.
    private void WarnIfMinGtMax()
    {
        if (TryInt(DetailMin, out var min) && TryInt(DetailMax, out var max) && min > max)
            Status = $"⚠ min ({min}) > max ({max}) — the validator flags this";
    }

    private void WarnUnknownEnum(string? value, string field, params string[] known)
    {
        var v = (value ?? "").Trim();
        if (v.Length > 0 && !known.Contains(v, StringComparer.OrdinalIgnoreCase))
            Status = $"⚠ unknown {field} \"{v}\" — expected one of: {string.Join(", ", known)}";
    }

    public void SaveFlag(string flag, bool value)
    {
        if (_suspendDetailPersist || SelectedEvent is not { } row) return;
        PushUndo();
        Report(_svc.SetFlag(row.Name, flag, value));
    }

    public void SavePosition(string position)
    {
        if (_suspendDetailPersist || SelectedEvent is not { } row) return;
        PushUndo();
        if (Report(_svc.SetPosition(row.Name, position ?? "")))
            WarnUnknownEnum(position, "position", "fixed", "player");
    }

    public void SaveLimit(string limit)
    {
        if (_suspendDetailPersist || SelectedEvent is not { } row) return;
        PushUndo();
        if (Report(_svc.SetLimit(row.Name, limit ?? "")))
            WarnUnknownEnum(limit, "limit", "custom", "child", "parent", "mixed");
    }

    public void SaveActive(bool active)
    {
        if (_suspendDetailPersist || SelectedEvent is not { } row) return;
        PushUndo();
        if (Report(_svc.SetActive(row.Name, active))) RefreshMasterRow(row.Name);
    }

    private void RefreshMasterRow(string name)
    {
        var ev = Model
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (ev is null) return;
        var row = _all.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        row.Nominal       = ev.Nominal;
        row.Min           = ev.Min;
        row.Max           = ev.Max;
        row.Lifetime      = ev.Lifetime;
        row.Active        = ev.Active;
        row.ChildrenCount = ev.Children.Count;
        row.Deletable     = ev.Deletable;
        row.InitRandom    = ev.InitRandom;
        row.RemoveDamaged = ev.RemoveDamaged;
    }

    public void AddChild()
    {
        if (SelectedEvent is not { } row) { Status = "✗ select an event first"; return; }
        var type = (NewChildType ?? "").Trim();
        if (type.Length == 0) { Status = "✗ child type must not be empty"; return; }
        if (!TryInt(NewChildMin, out var min)) { Status = "✗ min must be a non-negative integer"; return; }
        if (!TryInt(NewChildMax, out var max)) { Status = "✗ max must be a non-negative integer"; return; }
        if (!TryInt(NewChildLootMin, out var lootMin)) { Status = "✗ lootmin must be a non-negative integer"; return; }
        if (!TryInt(NewChildLootMax, out var lootMax)) { Status = "✗ lootmax must be a non-negative integer"; return; }

        PushUndo();
        if (Report(_svc.AddChild(row.Name, new EventChild(type, min, max, lootMin, lootMax))))
        {
            NewChildType = "";
            LoadDetailForSelected();
            RefreshMasterRow(row.Name);
        }
    }

    public void RemoveChild(EventChildRowVm? child)
    {
        if (SelectedEvent is not { } row || child is null) { Status = "✗ select a child to remove"; return; }
        PushUndo();
        if (Report(_svc.RemoveChild(row.Name, child.Type)))
        {
            LoadDetailForSelected();
            RefreshMasterRow(row.Name);
        }
    }

    /// <summary>An inline-edited child row committed — persist it (optionally renames type).</summary>
    public void CommitChildEdit(EventChildRowVm child)
    {
        if (_suspendDetailPersist || SelectedEvent is not { } row) return;
        if (string.IsNullOrWhiteSpace(child.Type)) { Status = "✗ child type must not be empty — reverting"; LoadDetailForSelected(); return; }
        if (!TryInt(child.MinText, out var min))      { Status = "✗ min must be a non-negative integer — reverting"; LoadDetailForSelected(); return; }
        if (!TryInt(child.MaxText, out var max))      { Status = "✗ max must be a non-negative integer — reverting"; LoadDetailForSelected(); return; }
        if (!TryInt(child.LootMinText, out var lootMin)) { Status = "✗ lootmin must be a non-negative integer — reverting"; LoadDetailForSelected(); return; }
        if (!TryInt(child.LootMaxText, out var lootMax)) { Status = "✗ lootmax must be a non-negative integer — reverting"; LoadDetailForSelected(); return; }

        PushUndo();
        var updated = new EventChild(child.Type, min, max, lootMin, lootMax);
        if (Report(_svc.SetChild(row.Name, child.OriginalType, updated)))
        {
            child.CommitType();
            RefreshMasterRow(row.Name);
        }
        else
        {
            LoadDetailForSelected();
        }
    }
}

/// <summary>One master-list row: an event with summary columns + its boolean flags (shown as a compact
/// letter strip like the Types grid: A D I R).</summary>
public sealed partial class EventRowVm : ObservableObject
{
    public EventRowVm(string name, int nominal, int min, int max, int lifetime, bool active, int childrenCount,
                      bool deletable, bool initRandom, bool removeDamaged)
    {
        Name = name;
        _nominal = nominal;
        _min = min;
        _max = max;
        _lifetime = lifetime;
        _active = active;
        _childrenCount = childrenCount;
        _deletable = deletable;
        _initRandom = initRandom;
        _removeDamaged = removeDamaged;
    }

    public string Name { get; }

    [ObservableProperty] private int _nominal;
    [ObservableProperty] private int _min;
    [ObservableProperty] private int _max;
    [ObservableProperty] private int _lifetime;
    [ObservableProperty] private bool _active;
    [ObservableProperty] private int _childrenCount;
    [ObservableProperty] private bool _deletable;
    [ObservableProperty] private bool _initRandom;
    [ObservableProperty] private bool _removeDamaged;
}

/// <summary>One editable child row in the children DataGrid.</summary>
public sealed partial class EventChildRowVm : ObservableObject
{
    public EventChildRowVm(string type, int min, int max, int lootMin, int lootMax)
    {
        _type = type;
        OriginalType = type;
        _minText = min.ToString();
        _maxText = max.ToString();
        _lootMinText = lootMin.ToString();
        _lootMaxText = lootMax.ToString();
    }

    /// <summary>The type as last persisted (used to locate the element when renaming).</summary>
    public string OriginalType { get; private set; }

    /// <summary>Adopt the current Type as the persisted baseline after a successful save.</summary>
    public void CommitType() => OriginalType = Type;

    /// <summary>Raised by the view when a cell commits, so the VM persists the edit.</summary>
    public event Action<EventChildRowVm>? Edited;

    public void Commit() => Edited?.Invoke(this);

    [ObservableProperty] private string _type;
    [ObservableProperty] private string _minText;
    [ObservableProperty] private string _maxText;
    [ObservableProperty] private string _lootMinText;
    [ObservableProperty] private string _lootMaxText;
}
