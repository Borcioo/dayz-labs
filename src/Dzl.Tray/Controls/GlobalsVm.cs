using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="GlobalsEditor"/> control (the Economy "Globals" tab): a filterable, editable
/// DataGrid of <c>&lt;var&gt;</c> rows from <c>db/globals.xml</c>. All edits route through
/// <see cref="GlobalsService"/> (never throws; snapshots a backup before each write). Per-tab undo/redo
/// snapshots the whole file's raw text before each mutation and restores it verbatim.
/// </summary>
public sealed partial class GlobalsVm : ObservableObject
{
    private readonly GlobalsService _svc;
    private readonly Func<string, bool> _confirm;

    /// <param name="configPath">The resolved dzl config path.</param>
    /// <param name="confirm">Modal yes/no confirmation (returns true on Yes).</param>
    public GlobalsVm(string configPath, Func<string, bool> confirm)
    {
        _svc = new GlobalsService(configPath);
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

    [ObservableProperty] private string _status = "";

    [ObservableProperty] private string _filter = "";

    partial void OnFilterChanged(string value) => ApplyFilter();

    // new-var form
    [ObservableProperty] private string _newVarName = "";
    [ObservableProperty] private string _newVarValue = "";
    [ObservableProperty] private int _newVarType = 0;

    /// <summary>True when the file is resolvable (a mission is active) — gates the editor UI.</summary>
    public bool HasFile => _svc.GlobalsPath() is not null;

    /// <summary>The resolved file path for the status/header (or a hint when unresolved).</summary>
    public string FileLabel => _svc.GlobalsPath() ?? "(no globals.xml — pick/scaffold a server mission)";

    // ------------------------------------------------------------------
    // Load
    // ------------------------------------------------------------------

    /// <summary>(Re)load all vars from disk. Clears undo/redo history.</summary>
    public void Reload()
    {
        _undo.Clear();
        _redo.Clear();
        NotifyHistory();
        LoadKeepingSelection();
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(FileLabel));
    }

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
    // Undo/redo (raw-file snapshots)
    // ------------------------------------------------------------------

    private const int UndoCap = 50;
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void PushUndo()
    {
        var raw = _svc.ReadRaw();
        if (raw is null) return;
        _undo.Add(raw);
        if (_undo.Count > UndoCap) _undo.RemoveAt(0);
        _redo.Clear();
        NotifyHistory();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Count == 0) return;
        var cur = _svc.ReadRaw();
        var prev = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        if (cur is not null) _redo.Add(cur);
        var (ok, msg) = _svc.WriteRaw(prev);
        Status = ok ? "↶ undo" : "✗ " + msg;
        LoadKeepingSelection();
        NotifyHistory();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redo.Count == 0) return;
        var cur = _svc.ReadRaw();
        var next = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        if (cur is not null) _undo.Add(cur);
        var (ok, msg) = _svc.WriteRaw(next);
        Status = ok ? "↷ redo" : "✗ " + msg;
        LoadKeepingSelection();
        NotifyHistory();
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
        var (ok, msg) = _svc.SetVar(name, NewVarType, NewVarValue ?? "");
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
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
        var (ok, msg) = _svc.RemoveVar(row.Name);
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok) LoadKeepingSelection();
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
        var (ok, msg) = _svc.SetVar(row.Name, row.Type, row.Value ?? "");
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
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
