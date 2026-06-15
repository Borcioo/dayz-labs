using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

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

    /// <summary>Engine globals not yet present in the file — the only names the "Add" affordance offers
    /// (globals.xml is a closed engine vocabulary, so you can't invent variables, only surface a missing one).</summary>
    public ObservableCollection<string> MissingKnown { get; } = new();

    /// <summary>The known-missing variable picked in the Add dropdown.</summary>
    [ObservableProperty] private string? _selectedMissing;

    public bool HasMissingKnown => MissingKnown.Count > 0;

    // Detail pane (right column) — editing moved off the grid, matching the other CE tabs.
    private bool _suspendDetailSync;

    /// <summary>True when a var is selected — gates the detail pane's rename box + value card.</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>True when the selected var is a standard engine global — its name + type are fixed (the name
    /// IS the engine key) and it can't be removed, only reset to default.</summary>
    public bool SelectedIsKnown => SelectedRow?.IsKnown ?? false;

    /// <summary>Name/type are editable only for a non-standard (custom) selected var.</summary>
    public bool CanEditIdentity => SelectedRow is { IsKnown: false };

    /// <summary>Editable name of the selected var (detail pane inline-rename box; Enter / button commits).</summary>
    [ObservableProperty] private string _renameText = "";
    /// <summary>0 = int, 1 = float — bound to the detail Type combo; commits on change.</summary>
    [ObservableProperty] private int _detailType;
    /// <summary>The selected var's value; commits on LostFocus / Enter.</summary>
    [ObservableProperty] private string _detailValue = "";

    partial void OnSelectedRowChanged(GlobalVarRowVm? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIsKnown));
        OnPropertyChanged(nameof(CanEditIdentity));
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
        if (row.IsKnown) { Status = $"✗ \"{row.Name}\" is a standard engine variable — its name is fixed"; return; }
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
        // A standard global's type is engine-fixed (docs: "types of existing variables should never be
        // modified") — persist with the catalog type regardless of the (disabled) detail combo.
        var type = row.IsKnown ? (GlobalsCatalog.Find(row.Name)?.Type ?? DetailType) : DetailType;
        if (!IsValidValue(DetailValue, type)) { Status = ValueError(type); return; }
        PushUndo();
        if (Report(_svc.SetVar(row.Name, type, DetailValue ?? "")))
        {
            row.Type = type;
            row.Value = DetailValue ?? "";
        }
    }

    /// <summary>Reset a standard global back to its engine default value (the non-destructive alternative to
    /// removing it — a missing/absent global already falls back to this default).</summary>
    public void ResetToDefault(GlobalVarRowVm? row)
    {
        row ??= SelectedRow;
        if (row is null) { Status = "✗ select a var to reset"; return; }
        if (GlobalsCatalog.Find(row.Name) is not { } def)
        { Status = $"✗ \"{row.Name}\" is not a standard variable — it has no engine default"; return; }
        PushUndo();
        if (Report(_svc.SetVar(def.Name, def.Type, def.Default)))
        {
            LoadKeepingSelection();
            SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Name, def.Name, StringComparison.OrdinalIgnoreCase));
            Status = $"✓ {def.Name} reset to default ({def.Default})";
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

        RefreshMissingKnown();
        ApplyFilter();

        SelectedRow = Rows.FirstOrDefault(r => r.Name == prevName) ?? Rows.FirstOrDefault();
    }

    /// <summary>Recompute the engine globals not yet present in the file (the Add dropdown's source).</summary>
    private void RefreshMissingKnown()
    {
        var present = new HashSet<string>(_allRows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        MissingKnown.Clear();
        foreach (var d in GlobalsCatalog.All)
            if (!present.Contains(d.Name)) MissingKnown.Add(d.Name);
        SelectedMissing = MissingKnown.FirstOrDefault();
        OnPropertyChanged(nameof(HasMissingKnown));
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

    /// <summary>Add the known-but-missing variable picked in the Add dropdown, seeded with its engine default
    /// + fixed type. globals.xml is a closed engine vocabulary, so only documented names can be added.</summary>
    public void AddKnown()
    {
        var name = (SelectedMissing ?? "").Trim();
        if (name.Length == 0) { Status = "✗ pick a standard variable to add"; return; }
        if (GlobalsCatalog.Find(name) is not { } def)
        { Status = $"✗ \"{name}\" is not a known engine variable"; return; }
        if (_allRows.Any(r => string.Equals(r.Name, def.Name, StringComparison.OrdinalIgnoreCase)))
        { Status = $"✗ \"{def.Name}\" is already present"; return; }

        PushUndo();
        if (Report(_svc.SetVar(def.Name, def.Type, def.Default)))
        {
            LoadKeepingSelection();
            SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Name, def.Name, StringComparison.OrdinalIgnoreCase));
            Status = $"✓ added {def.Name} ({def.Default})";
        }
    }

    /// <summary>Remove a variable. Standard engine globals are NOT removable (removal only reverts them to the
    /// engine default — use <see cref="ResetToDefault"/>); only a custom/non-standard key can be deleted.</summary>
    public void RemoveVar(GlobalVarRowVm? row)
    {
        row ??= SelectedRow;
        if (row is null) { Status = "✗ select a var to remove"; return; }
        if (row.IsKnown)
        { Status = $"✗ \"{row.Name}\" is a standard engine variable — reset it to default instead of removing it"; return; }
        if (!_confirm($"Remove the custom variable \"{row.Name}\"?")) return;

        PushUndo();
        if (Report(_svc.RemoveVar(row.Name))) LoadKeepingSelection();
    }

    /// <summary>Remove the currently selected var (kept for the detail-pane action; delegates to <see cref="RemoveVar"/>).</summary>
    public void RemoveSelectedVar() => RemoveVar(SelectedRow);
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
        var def = GlobalsCatalog.Find(name);
        IsKnown = def is not null;
        DefaultValue = def?.Default ?? "";
        Description = def?.Description ?? "Custom (non-standard) variable — not part of the engine vocabulary.";
    }

    /// <summary>True when this is an engine-defined global (has a fixed type + default); false for a custom key.</summary>
    public bool IsKnown { get; }

    /// <summary>Custom (non-standard) var: the only kind that may be renamed or removed.</summary>
    public bool IsCustom => !IsKnown;

    /// <summary>The engine default value (empty for a custom var).</summary>
    public string DefaultValue { get; }

    /// <summary>Short description for the row tooltip (from the catalog, or a custom-var note).</summary>
    public string Description { get; }

    /// <summary>Row tooltip: description + engine default for known vars.</summary>
    public string Tooltip => IsKnown ? $"{Description}\nEngine default: {DefaultValue}" : Description;

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
