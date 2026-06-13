using System.Collections.ObjectModel;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>One editable dictionary list (Categories / Tags / Usage / Value) shown as a column in the
/// <see cref="DictionaryManager"/>. Holds the live names for one <see cref="LimitsKind"/> plus the typed
/// "add" buffer. Edits route back to the host through events so the host can call the
/// <c>DictionaryService</c>, refresh the Types editor suggestions, and re-lint.</summary>
public sealed class DictionaryListVm : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public DictionaryListVm(LimitsKind kind, string title, string hint)
    {
        Kind = kind;
        Title = title;
        Hint = hint;
    }

    public LimitsKind Kind { get; }
    public string Title { get; }
    public string Hint { get; }

    /// <summary>Live dictionary names for this kind (sorted, case-insensitive).</summary>
    public ObservableCollection<string> Names { get; } = new();

    /// <summary>"N entries" header summary.</summary>
    public string CountLabel => $"{Names.Count} {(Names.Count == 1 ? "entry" : "entries")}";

    private string _newName = "";
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }

    private string? _selected;
    public string? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    /// <summary>Repopulate from a source set (called by the host after every reload/edit).</summary>
    public void Fill(System.Collections.Generic.IEnumerable<string> src)
    {
        var sel = Selected;
        Names.Clear();
        foreach (var v in src) Names.Add(v);
        OnPropertyChanged(nameof(CountLabel));
        if (sel is not null && Names.Contains(sel)) Selected = sel;
    }

    /// <summary>Raised when the user requests Add (carries the typed name).</summary>
    public event Action<DictionaryListVm, string>? AddRequested;
    /// <summary>Raised when the user requests Remove (carries the target name).</summary>
    public event Action<DictionaryListVm, string>? RemoveRequested;
    /// <summary>Raised when the user requests Rename (carries old + new name).</summary>
    public event Action<DictionaryListVm, string, string>? RenameRequested;

    public void RequestAdd()
    {
        var n = (NewName ?? "").Trim();
        if (n.Length == 0) return;
        AddRequested?.Invoke(this, n);
    }

    public void RequestRemove(string? name)
    {
        var n = (name ?? Selected ?? "").Trim();
        if (n.Length == 0) return;
        RemoveRequested?.Invoke(this, n);
    }

    public void RequestRename(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();
        if (oldName.Length == 0 || newName.Length == 0 || oldName == newName) return;
        RenameRequested?.Invoke(this, oldName, newName);
    }
}
