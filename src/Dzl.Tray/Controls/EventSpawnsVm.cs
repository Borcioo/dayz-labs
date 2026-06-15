using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="EventSpawnsEditor"/> control (Events "Event Spawns" tab): edits cfgeventspawns.xml —
/// per dynamic-event spawn positions. A filterable event list (master) drives the selected event's X/Z/A
/// positions grid (detail). All edits route through <see cref="EventSpawnsService"/>; per-tab undo/redo + status
/// from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class EventSpawnsVm : RawXmlEditorVm
{
    private readonly EventSpawnsService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspend;

    public EventSpawnsVm(string configPath, Func<string, bool> confirm)
        : this(new EventSpawnsService(configPath), confirm) { }

    private EventSpawnsVm(EventSpawnsService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.EventSpawnsPath,
               "(no cfgeventspawns.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    private List<EventSpawn>? _model;
    private List<EventSpawn> Model => _model ??= _svc.Load();
    protected override void InvalidateModelCache() => _model = null;

    private readonly List<EventSpawnRowVm> _allEvents = new();
    public ObservableCollection<EventSpawnRowVm> Events { get; } = new();
    [ObservableProperty] private EventSpawnRowVm? _selectedEvent;
    [ObservableProperty] private string _filter = "";
    partial void OnFilterChanged(string value) => ApplyFilter();

    public ObservableCollection<EventPosVm> Positions { get; } = new();
    [ObservableProperty] private string _newEventName = "";

    protected override void ReloadView()
    {
        var prev = SelectedEvent?.Name;
        _allEvents.Clear();
        foreach (var ev in Model.Where(e => !string.IsNullOrEmpty(e.Name)))
            _allEvents.Add(new EventSpawnRowVm(ev.Name, ev.Positions.Count));
        ApplyFilter();
        SelectedEvent = Events.FirstOrDefault(e => string.Equals(e.Name, prev, StringComparison.OrdinalIgnoreCase))
                        ?? Events.FirstOrDefault();
        LoadPositions();
    }

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Events.Clear();
        foreach (var e in _allEvents)
            if (f.Length == 0 || e.Name.Contains(f, StringComparison.OrdinalIgnoreCase)) Events.Add(e);
    }

    partial void OnSelectedEventChanged(EventSpawnRowVm? value) => LoadPositions();

    private void LoadPositions()
    {
        _suspend = true;
        try
        {
            foreach (var p in Positions) p.Edited -= OnPosEdited;
            Positions.Clear();
            if (SelectedEvent is { } ev && Model.FirstOrDefault(e => string.Equals(e.Name, ev.Name, StringComparison.OrdinalIgnoreCase)) is { } model)
                for (var i = 0; i < model.Positions.Count; i++)
                {
                    var pvm = new EventPosVm(i, model.Positions[i].X, model.Positions[i].Z, model.Positions[i].A);
                    pvm.Edited += OnPosEdited;
                    Positions.Add(pvm);
                }
        }
        finally { _suspend = false; }
    }

    protected override string? CaptureSelectionToken() => SelectedEvent?.Name;
    protected override void RestoreSelectionToken(string? token)
    {
        if (token is not null) SelectedEvent = Events.FirstOrDefault(e => string.Equals(e.Name, token, StringComparison.OrdinalIgnoreCase)) ?? SelectedEvent;
    }

    private static bool TryDouble(string raw, out double v) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    public void AddEvent()
    {
        var name = (NewEventName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ event name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.AddEvent(name)))
        {
            NewEventName = "";
            ReloadView();
            SelectedEvent = Events.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveSelectedEvent()
    {
        if (SelectedEvent is not { } ev) { Status = "✗ select an event to remove"; return; }
        if (!_confirm($"Remove the event \"{ev.Name}\" and all its spawn positions?")) return;
        PushUndo();
        if (Report(_svc.RemoveEvent(ev.Name))) ReloadView();
    }

    public void AddPos(string xText, string zText, string aText)
    {
        if (SelectedEvent is not { } ev) { Status = "✗ select an event first"; return; }
        if (!TryDouble(xText, out var x)) { Status = "✗ X must be a number"; return; }
        if (!TryDouble(zText, out var z)) { Status = "✗ Z must be a number"; return; }
        if (!TryDouble(string.IsNullOrWhiteSpace(aText) ? "0" : aText, out var a)) { Status = "✗ angle must be a number"; return; }
        PushUndo();
        if (Report(_svc.AddPos(ev.Name, x, z, a)))
        {
            LoadPositions();
            ev.PosCount = Positions.Count;
        }
    }

    public void RemovePos(EventPosVm? pos)
    {
        if (SelectedEvent is not { } ev || pos is null) { Status = "✗ select a position to remove"; return; }
        PushUndo();
        if (Report(_svc.RemovePos(ev.Name, pos.Index)))
        {
            LoadPositions();
            ev.PosCount = Positions.Count;
        }
    }

    private void OnPosEdited(EventPosVm pos)
    {
        if (_suspend || SelectedEvent is not { } ev) return;
        if (!TryDouble(pos.XText, out var x)) { Status = "✗ X must be a number"; return; }
        if (!TryDouble(pos.ZText, out var z)) { Status = "✗ Z must be a number"; return; }
        if (!TryDouble(pos.AText, out var a)) { Status = "✗ angle must be a number"; return; }
        PushUndo();
        if (!Report(_svc.SetPos(ev.Name, pos.Index, x, z, a))) LoadPositions();
    }
}

/// <summary>One event row (name + live position count) in the master list.</summary>
public sealed partial class EventSpawnRowVm : ObservableObject
{
    public EventSpawnRowVm(string name, int posCount) { Name = name; _posCount = posCount; }
    public string Name { get; }
    [ObservableProperty] private int _posCount;
}

/// <summary>One editable spawn position (X / Z / angle) of an event.</summary>
public sealed partial class EventPosVm : ObservableObject
{
    public EventPosVm(int index, double x, double z, double a)
    {
        Index = index;
        _xText = x.ToString(CultureInfo.InvariantCulture);
        _zText = z.ToString(CultureInfo.InvariantCulture);
        _aText = a.ToString(CultureInfo.InvariantCulture);
    }

    public int Index { get; }
    [ObservableProperty] private string _xText;
    [ObservableProperty] private string _zText;
    [ObservableProperty] private string _aText;

    public void Commit() => Edited?.Invoke(this);
    public event Action<EventPosVm>? Edited;
}
