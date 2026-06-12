using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
               "(no globals.xml — pick/scaffold a server mission)")
    {
        _svc = svc;
        _confirm = confirm;
    }

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    /// <summary>Add a new var from the new-var form.</summary>
    public void AddVar()
    {
        var name = (NewVarName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ var name must not be empty"; return; }

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

    /// <summary>Persist an inline-edited row after the DataGrid commits the edit.</summary>
    public void CommitRowEdit(GlobalVarRowVm row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
        {
            Status = "✗ var name must not be empty — reverting";
            LoadKeepingSelection();
            return;
        }

        PushUndo();
        // If the name changed we need rename + value update.
        if (!string.Equals(row.Name, row.OriginalName, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(row.OriginalName))
        {
            var (rok, rmsg) = _svc.RenameVar(row.OriginalName, row.Name);
            if (!rok) { Status = "✗ " + rmsg; LoadKeepingSelection(); return; }
        }
        // Upsert to commit type + value (and handle the case where name didn't change).
        if (Report(_svc.SetVar(row.Name, row.Type, row.Value ?? "")))
        {
            row.CommitName();
            LoadKeepingSelection();
            SelectedRow = Rows.FirstOrDefault(r => r.Name == row.Name);
        }
        else
        {
            LoadKeepingSelection();
        }
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
}
