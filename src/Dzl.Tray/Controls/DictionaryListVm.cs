using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>One editable dictionary (Categories / Tags / Usage / Value) in the <see cref="DictionaryManager"/>
/// dashboard. Holds its entries as editable row VMs (<see cref="DictEntryVm"/>) — renamed in place — plus the
/// add buffer and a search filter. Edits route back to the host through events so it can call the
/// <c>DictionaryService</c>, refresh the Types editor suggestions, and re-lint.</summary>
public sealed partial class DictionaryListVm : ObservableObject
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

    /// <summary>One-line CE meaning of this dictionary, shown under the detail header.</summary>
    public string Help => Kind switch
    {
        LimitsKind.Usage => "Usage = where an item can spawn (Town, Military, Police, Coast, …).",
        LimitsKind.Value => "Value = the loot tier an item belongs to (Tier1–Tier4, Unique).",
        LimitsKind.Tag => "Tag = secondary placement filter (floor, shelves, ground).",
        LimitsKind.Category => "Category = high-level item group (weapons, food, clothes, …).",
        _ => "",
    };

    /// <summary>All entries for this kind.</summary>
    public ObservableCollection<DictEntryVm> Names { get; } = new();

    /// <summary>The search-filtered slice of <see cref="Names"/> shown in the detail list.</summary>
    public ObservableCollection<DictEntryVm> View { get; } = new();

    private string _filter = "";
    /// <summary>Substring search over the entries (case-insensitive). Rebuilds <see cref="View"/>.</summary>
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) RebuildView(); } }

    private void RebuildView()
    {
        View.Clear();
        var f = (_filter ?? "").Trim();
        foreach (var e in Names)
            if (f.Length == 0 || e.Name.Contains(f, StringComparison.OrdinalIgnoreCase))
                View.Add(e);
    }

    /// <summary>"N entries" header summary.</summary>
    public string CountLabel => $"{Names.Count} {(Names.Count == 1 ? "entry" : "entries")}";

    private string _newName = "";
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }

    [ObservableProperty] private DictEntryVm? _selected;

    /// <summary>Repopulate from a source set (called by the host after every reload/edit). Re-wraps each name
    /// in a fresh row VM (so any in-progress edit state is dropped).</summary>
    public void Fill(System.Collections.Generic.IEnumerable<string> src)
    {
        var selName = Selected?.Name;
        Names.Clear();
        foreach (var v in src) Names.Add(new DictEntryVm(v));
        RebuildView();
        OnPropertyChanged(nameof(CountLabel));
        if (selName is not null) Selected = Names.FirstOrDefault(e => e.Name == selName);
    }

    /// <summary>Raised when the user requests Add (carries the typed name).</summary>
    public event Action<DictionaryListVm, string>? AddRequested;
    /// <summary>Raised when the user requests Remove (carries the target name).</summary>
    public event Action<DictionaryListVm, string>? RemoveRequested;
    /// <summary>Raised when an in-place rename commits (carries old + new name).</summary>
    public event Action<DictionaryListVm, string, string>? RenameRequested;

    public void RequestAdd()
    {
        var n = (NewName ?? "").Trim();
        if (n.Length == 0) return;
        AddRequested?.Invoke(this, n);
    }

    public void RequestRemove(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return;
        RemoveRequested?.Invoke(this, n);
    }

    /// <summary>Enter in-place edit on an entry (double-click / pencil).</summary>
    public void BeginEdit(DictEntryVm e)
    {
        Selected = e;
        e.IsEditing = true;
    }

    /// <summary>Commit an in-place rename: revert on empty/unchanged, else ask the host to rename (which
    /// reloads + re-wraps the entries).</summary>
    public void CommitEdit(DictEntryVm e)
    {
        if (!e.IsEditing) return;
        e.IsEditing = false;
        var newName = (e.Name ?? "").Trim();
        if (newName.Length == 0 || string.Equals(newName, e.OriginalName, StringComparison.Ordinal))
        {
            e.Name = e.OriginalName;
            return;
        }
        RenameRequested?.Invoke(this, e.OriginalName, newName);
    }

    /// <summary>Abandon an in-place edit (Esc) — restore the persisted name.</summary>
    public void CancelEdit(DictEntryVm e)
    {
        e.Name = e.OriginalName;
        e.IsEditing = false;
    }
}

/// <summary>One editable entry row: its name + whether it's currently in inline-edit mode.</summary>
public sealed partial class DictEntryVm : ObservableObject
{
    public DictEntryVm(string name)
    {
        _name = name;
        OriginalName = name;
    }

    /// <summary>The name as last persisted (the rename source key).</summary>
    public string OriginalName { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEditing;
}
