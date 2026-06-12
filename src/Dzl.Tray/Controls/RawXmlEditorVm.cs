using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dzl.Tray.Controls;

/// <summary>
/// Shared scaffolding for the per-tab CE file editors (<see cref="GlobalsVm"/>, <see cref="EventsVm"/>,
/// <see cref="RandomPresetsVm"/>, <see cref="SpawnableTypesVm"/>, <see cref="PlayerSpawnsVm"/>):
/// raw-file-snapshot undo/redo over the service's <c>ReadRaw</c>/<c>WriteRaw</c>, the ✓/✗ status line,
/// and the <see cref="HasFile"/>/<see cref="FileLabel"/> empty-state pair.
/// <para>
/// The base owns ONLY the raw-snapshot undo model + status. Domain commands, selection, and how detail
/// edits persist stay in each VM — e.g. <see cref="EventsVm"/> persists detail fields per-commit (guarded
/// by its own suspend flag), while <c>TypesEditorVm</c> deliberately uses a different entry-snapshot undo
/// model and does not derive from this class. Do not merge those models into the base.
/// </para>
/// </summary>
public abstract partial class RawXmlEditorVm : ObservableObject
{
    private readonly Func<string?> _readRaw;
    private readonly Func<string, (bool ok, string msg)> _writeRaw;
    private readonly Func<string?> _filePath;
    private readonly string _missingHint;

    /// <param name="readRaw">Reads the current raw file text (null when absent/unresolvable).</param>
    /// <param name="writeRaw">Overwrites the file verbatim (snapshots a backup first); returns ok + message.</param>
    /// <param name="filePath">Resolves the edited file's path (null when no mission is active).</param>
    /// <param name="missingHint">Shown as <see cref="FileLabel"/> when the file is unresolvable.</param>
    protected RawXmlEditorVm(
        Func<string?> readRaw,
        Func<string, (bool ok, string msg)> writeRaw,
        Func<string?> filePath,
        string missingHint)
    {
        _readRaw = readRaw;
        _writeRaw = writeRaw;
        _filePath = filePath;
        _missingHint = missingHint;
    }

    // ------------------------------------------------------------------
    // Status line
    // ------------------------------------------------------------------

    /// <summary>One-line feedback under the editor ("✓ saved …" / "✗ …").</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>Set <see cref="Status"/> from a service result ("✓ msg" on success, "✗ msg" on
    /// failure) and return ok so call sites can chain follow-up work.</summary>
    protected bool Report((bool ok, string msg) result)
    {
        Status = (result.ok ? "✓ " : "✗ ") + result.msg;
        return result.ok;
    }

    // ------------------------------------------------------------------
    // File presence
    // ------------------------------------------------------------------

    /// <summary>True when the file is resolvable (a mission is active) — gates the editor UI.</summary>
    public bool HasFile => _filePath() is not null;

    /// <summary>The resolved file path for the status/header (or a hint when unresolved).</summary>
    public string FileLabel => _filePath() ?? _missingHint;

    // ------------------------------------------------------------------
    // Reload
    // ------------------------------------------------------------------

    /// <summary>(Re)load from disk: clears undo/redo history, reloads the view keeping selection,
    /// and re-evaluates <see cref="HasFile"/>/<see cref="FileLabel"/>.</summary>
    public virtual void Reload()
    {
        _undo.Clear();
        _redo.Clear();
        NotifyHistory();
        InvalidateModelCache();
        ReloadView();
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(FileLabel));
    }

    /// <summary>Reload the VM's collections from disk keeping the current selection (and, where the
    /// tab has one, the detail pane). Called by <see cref="Reload"/> and after undo/redo restores.</summary>
    protected abstract void ReloadView();

    /// <summary>Subclasses that cache the parsed model between reads override this to drop the cache.
    /// The base calls it whenever the file is about to change or just changed (every <see cref="PushUndo"/>
    /// — including seed-on-first-edit writes where ReadRaw is still null — every undo/redo restore, and
    /// <see cref="Reload"/>), so a cached model can never go stale through edits made via this VM.
    /// Default: no-op.</summary>
    protected virtual void InvalidateModelCache() { }

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

    /// <summary>Snapshot the current file before a mutation so it can be undone. Call right before
    /// every service write. Also invalidates the parsed-model cache for the write that follows.</summary>
    protected void PushUndo()
    {
        InvalidateModelCache();
        var raw = _readRaw();
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
        var cur = _readRaw();
        var prev = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        if (cur is not null) _redo.Add(cur);
        var (ok, msg) = _writeRaw(prev);
        Status = ok ? "↶ undo" : "✗ " + msg;
        InvalidateModelCache();
        ReloadView();
        NotifyHistory();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redo.Count == 0) return;
        var cur = _readRaw();
        var next = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        if (cur is not null) _undo.Add(cur);
        var (ok, msg) = _writeRaw(next);
        Status = ok ? "↷ redo" : "✗ " + msg;
        InvalidateModelCache();
        ReloadView();
        NotifyHistory();
    }

    // ------------------------------------------------------------------
    // Shared parsing
    // ------------------------------------------------------------------

    /// <summary>Parse a CE chance: an invariant-culture float in 0..1.</summary>
    protected static bool TryChance(string raw, out double value) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && value is >= 0.0 and <= 1.0;
}
