using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="GlobalsEditor"/> control (the Economy "Globals" tab): a filterable, editable
/// DataGrid of <c>&lt;var&gt;</c> rows from <c>db/globals.xml</c>. All edits route through
/// <see cref="GlobalsService"/> (never throws; snapshots a backup before each write). Per-tab
/// undo/redo + the status line come from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class GlobalsVm : RawXmlEditorVm
{
    private readonly GlobalsService _svc;
    private readonly Func<string, bool> _confirm;

    /// <param name="configPath">The resolved dzl config path.</param>
    /// <param name="confirm">Modal yes/no confirmation (returns true on Yes).</param>
    public GlobalsVm(string configPath, Func<string, bool> confirm)
        : this(new GlobalsService(configPath), confirm) { }

    private GlobalsVm(GlobalsService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.GlobalsPath,
               "(no globals.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    /// <summary>All var rows loaded from disk.</summary>
    private readonly List<GlobalVarRowVm> _allRows = new();

    /// <summary>The filtered view shown in the DataGrid.</summary>
    public ObservableCollection<GlobalVarRowVm> Rows { get; } = new();

    [ObservableProperty] private GlobalVarRowVm? _selectedRow;

    [ObservableProperty] private string _filter = "";

    partial void OnFilterChanged(string value) => ApplyFilter();

    // new-var form
    [ObservableProperty] private string _newVarName = "";
    [ObservableProperty] private string _newVarValue = "";
    [ObservableProperty] private int _newVarType = 0;

    // Detail pane (right column) — editing moved off the grid, matching the other CE tabs.
    private bool _suspendDetailSync;

    /// <summary>True when a var is selected — gates the detail pane's rename box + value card.</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>Editable name of the selected var (detail pane inline-rename box; Enter / button commits).</summary>
    [ObservableProperty] private string _renameText = "";
    /// <summary>0 = int, 1 = float — bound to the detail Type combo; commits on change.</summary>
    [ObservableProperty] private int _detailType;
    /// <summary>The selected var's value; commits on LostFocus / Enter.</summary>
    [ObservableProperty] private string _detailValue = "";

    partial void OnSelectedRowChanged(GlobalVarRowVm? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _suspendDetailSync = true;
        RenameText  = value?.Name ?? "";
        DetailType  = value?.Type ?? 0;
        DetailValue = value?.Value ?? "";
        _suspendDetailSync = false;
    }

    /// <summary>Commit the detail pane's inline-rename box. No-op when unchanged.</summary>
    public void CommitRename()
    {
        if (SelectedRow is not { } row) { Status = "✗ select a var to rename"; return; }
        var newName = (RenameText ?? "").Trim();
        if (newName.Length == 0) { Status = "✗ name must not be empty"; return; }
        if (string.Equals(newName, row.Name, StringComparison.Ordinal)) return;

        PushUndo();
        var (ok, msg) = _svc.RenameVar(row.Name, newName);
        if (!ok) { Status = "✗ " + msg; return; }
        LoadKeepingSelection();
        SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase));
        Status = $"✓ renamed to {newName}";
    }

    /// <summary>Persist the detail pane's Type + Value onto the selected var (called when a field commits).</summary>
    public void SaveDetail()
    {
        if (_suspendDetailSync || SelectedRow is not { } row) return;
        if (!IsValidValue(DetailValue, DetailType)) { Status = ValueError(DetailType); return; }
        PushUndo();
        if (Report(_svc.SetVar(row.Name, DetailType, DetailValue ?? "")))
        {
            row.Type = DetailType;
            row.Value = DetailValue ?? "";
        }
    }

    /// <summary>A globals value must parse as a number: an integer when type=0 (int), otherwise a float.
    /// Stops non-numeric garbage (e.g. "potato") and decimals-under-int from being written silently —
    /// previously only a non-blocking lint warning caught it.</summary>
    private static bool IsValidValue(string? raw, int type)
    {
        raw = (raw ?? "").Trim();
        return type == 0
            ? int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static string ValueError(int type) =>
        type == 0 ? "✗ value must be a whole number (int type)" : "✗ value must be a number";

    /// <inheritdoc/>
    protected override void ReloadView() => LoadKeepingSelection();

    private void LoadKeepingSelection()
    {
        var prevName = SelectedRow?.Name;

        _allRows.Clear();
        foreach (var v in _svc.Load())
            _allRows.Add(new GlobalVarRowVm(v.Name, v.Type, v.Value));

        ApplyFilter();

        SelectedRow = Rows.FirstOrDefault(r => r.Name == prevName) ?? Rows.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Rows.Clear();
        foreach (var r in _allRows)
        {
            if (f.Length == 0 || r.Name.Contains(f, StringComparison.OrdinalIgnoreCase))
                Rows.Add(r);
        }
    }

    /// <summary>Select the var named <paramref name="name"/> (e.g. from a dashboard finding click), clearing
    /// the filter only if it would hide the row. Selects the entry directly — does NOT filter the list.</summary>
    public void SelectByEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!Rows.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            Filter = "";
        SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Add a new var from the new-var form.</summary>
    public void AddVar()
    {
        var name = (NewVarName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ var name must not be empty"; return; }
        // SetVar is an upsert; guard here so "Add" reports a duplicate instead of silently overwriting an
        // existing var's type/value (consistent with the other CE Add commands rejecting duplicates).
        if (_svc.Load().Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        { Status = $"✗ var \"{name}\" already exists"; return; }
        if (!IsValidValue(NewVarValue, NewVarType)) { Status = ValueError(NewVarType); return; }

        PushUndo();
        if (Report(_svc.SetVar(name, NewVarType, NewVarValue ?? "")))
        {
            NewVarName = "";
            NewVarValue = "";
            NewVarType = 0;
            LoadKeepingSelection();
            SelectedRow = Rows.FirstOrDefault(r => r.Name == name);
        }
    }

    /// <summary>Remove the currently selected var.</summary>
    public void RemoveSelectedVar()
    {
        if (SelectedRow is not { } row) { Status = "✗ select a var to remove"; return; }
        if (!_confirm($"Remove the global var \"{row.Name}\"?")) return;

        PushUndo();
        if (Report(_svc.RemoveVar(row.Name))) LoadKeepingSelection();
    }

}

/// <summary>One editable DataGrid row for a global var.</summary>
public sealed partial class GlobalVarRowVm : ObservableObject
{
    public GlobalVarRowVm(string name, int type, string value)
    {
        _name = name;
        OriginalName = name;
        _type = type;
        _value = value;
    }

    /// <summary>The name as last persisted (used to locate the element when renaming).</summary>
    public string OriginalName { get; private set; }

    /// <summary>Adopt the current Name as the persisted baseline after a successful save.</summary>
    public void CommitName() => OriginalName = Name;

    [ObservableProperty] private string _name;
    [ObservableProperty] private int _type;
    [ObservableProperty] private string _value;

    /// <summary>Friendly type for the master grid (the raw value is 0/1).</summary>
    public string TypeLabel => Type == 1 ? "float" : "int";
    partial void OnTypeChanged(int value) => OnPropertyChanged(nameof(TypeLabel));
}
