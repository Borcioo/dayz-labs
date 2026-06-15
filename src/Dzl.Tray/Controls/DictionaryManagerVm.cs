using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>Backs the <see cref="DictionaryManager"/> control: the four editable base lists
/// (Categories / Tags / Usage / Value from <c>cfglimitsdefinition.xml</c>) plus the named-combos
/// section (<c>cfglimitsdefinitionuser.xml</c>). All edits go through <see cref="DictionaryService"/>
/// (never throws — returns ok+message), then the host's <see cref="OnDictionaryChanged"/> callback
/// refreshes the Types editor's suggestion collections and re-lints.
///
/// Self-contained so it doesn't bloat MainViewModel; the host wires three hooks in the ctor:
/// the config path, a "refresh Types editor" action, and a "count types using value X of kind K"
/// probe (for the remove-in-use safety warning).</summary>
public sealed partial class DictionaryManagerVm : ObservableObject
{
    private readonly DictionaryService _svc;
    private readonly Action _onDictionaryChanged;
    private readonly Func<LimitsKind, string, int> _usageCount;
    private readonly Func<string, bool> _confirm;

    /// <param name="configPath">The resolved dzl config path.</param>
    /// <param name="onDictionaryChanged">Called after any successful edit so the host can re-pull the
    /// Types editor's suggestion lists (LimitsUsage/Value/Category/Tag) and re-lint.</param>
    /// <param name="usageCount">Counts how many loaded types use value <c>name</c> of the given kind
    /// (drives the remove-in-use warning).</param>
    /// <param name="confirm">Modal yes/no confirmation (returns true on Yes).</param>
    public DictionaryManagerVm(
        string configPath,
        Action onDictionaryChanged,
        Func<LimitsKind, string, int> usageCount,
        Func<string, bool> confirm)
    {
        _svc = new DictionaryService(configPath);
        _onDictionaryChanged = onDictionaryChanged;
        _usageCount = usageCount;
        _confirm = confirm;

        Categories = MakeList(LimitsKind.Category, "Categories", "add category…");
        Tags = MakeList(LimitsKind.Tag, "Tags", "add tag…");
        Usage = MakeList(LimitsKind.Usage, "Usage flags", "add usage…");
        Value = MakeList(LimitsKind.Value, "Value (tiers)", "add tier…");
        Lists = new[] { Categories, Tags, Usage, Value };
        _selectedList = Categories;   // dashboard master-detail: one section active at a time
    }

    public DictionaryListVm Categories { get; }
    public DictionaryListVm Tags { get; }
    public DictionaryListVm Usage { get; }
    public DictionaryListVm Value { get; }
    public IReadOnlyList<DictionaryListVm> Lists { get; }

    // Nav-rail selection: either one base list is active (SelectedList) OR the combos section (CombosSelected).
    [ObservableProperty] private DictionaryListVm? _selectedList;
    [ObservableProperty] private bool _combosSelected;

    partial void OnSelectedListChanged(DictionaryListVm? value)
    {
        if (value is not null) CombosSelected = false;   // picking a base list leaves the combos view
    }

    /// <summary>Switch the detail pane to the Named-combos editor (deselects the base-list rail).</summary>
    [RelayCommand]
    private void ShowCombos()
    {
        CombosSelected = true;
        SelectedList = null;
    }

    private DictionaryListVm MakeList(LimitsKind kind, string title, string hint)
    {
        var l = new DictionaryListVm(kind, title, hint);
        l.AddRequested += (list, name) => DoAdd(list, name);
        l.RemoveRequested += (list, name) => DoRemove(list, name);
        l.RenameRequested += (list, oldN, newN) => DoRename(list, oldN, newN);
        return l;
    }

    [ObservableProperty] private string _status = "";

    /// <summary>The live <see cref="LimitsDef"/> last loaded (so combos can source members from the base lists).</summary>
    public LimitsDef Limits { get; private set; } = LimitsDef.Empty;

    /// <summary>(Re)load every list + the combos from disk. Called on tab activation.</summary>
    public void Reload()
    {
        Limits = _svc.Load();
        Categories.Fill(Sorted(Limits.Category));
        Tags.Fill(Sorted(Limits.Tag));
        Usage.Fill(Sorted(Limits.Usage));
        Value.Fill(Sorted(Limits.Value));
        ReloadCombos();
    }

    private static IEnumerable<string> Sorted(IReadOnlySet<string> s)
        => s.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

    private DictionaryListVm? FindList(LimitsKind kind) => Lists.FirstOrDefault(l => l.Kind == kind);

    private void DoAdd(DictionaryListVm list, string name)
    {
        var (ok, msg) = _svc.AddName(list.Kind, name);
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok) { list.NewName = ""; AfterEdit(); }
    }

    private void DoRemove(DictionaryListVm list, string name)
    {
        var inUse = _usageCount(list.Kind, name);
        var prompt = inUse > 0
            ? $"\"{name}\" is used by {inUse} loaded type(s). Removing it from the dictionary will make those types reference an unknown {list.Kind} (lint will flag them).\n\nRemove anyway?"
            : $"Remove \"{name}\" from the {list.Kind} dictionary?";
        if (!_confirm(prompt)) return;
        var (ok, msg) = _svc.RemoveName(list.Kind, name);
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok) AfterEdit();
    }

    private void DoRename(DictionaryListVm list, string oldName, string newName)
    {
        var (ok, msg) = _svc.RenameName(list.Kind, oldName, newName);
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok) AfterEdit();
    }

    /// <summary>Reload from disk + refresh the Types editor suggestions + re-lint.</summary>
    private void AfterEdit()
    {
        Reload();
        _onDictionaryChanged();
    }

    // Combos = named usage/value groups from cfglimitsdefinitionuser.xml.
    public ObservableCollection<ComboVm> Combos { get; } = new();

    [ObservableProperty] private ComboVm? _selectedCombo;
    [ObservableProperty] private string _newComboName = "";
    /// <summary>Kind for the new-combo form: true = Usage, false = Value.</summary>
    [ObservableProperty] private bool _newComboIsUsage = true;

    /// <summary>Base usage names (suggestions for a Usage combo's member chips).</summary>
    public ObservableCollection<string> UsageMemberSource { get; } = new();
    /// <summary>Base value names (suggestions for a Value combo's member chips).</summary>
    public ObservableCollection<string> ValueMemberSource { get; } = new();

    private void ReloadCombos()
    {
        Fill(UsageMemberSource, Sorted(Limits.Usage));
        Fill(ValueMemberSource, Sorted(Limits.Value));

        var prevName = SelectedCombo?.Name;
        var prevKind = SelectedCombo?.Kind;
        Combos.Clear();
        foreach (var g in _svc.LoadGroups().OrderBy(g => g.Kind).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Combos.Add(new ComboVm(g.Kind, g.Name, g.Members));
        SelectedCombo = Combos.FirstOrDefault(c => c.Name == prevName && c.Kind == prevKind)
                        ?? Combos.FirstOrDefault();

        static void Fill(ObservableCollection<string> target, IEnumerable<string> src)
        {
            target.Clear();
            foreach (var v in src) target.Add(v);
        }
    }

    /// <summary>Member-suggestion source for the currently selected combo (matches its kind).</summary>
    public ObservableCollection<string> SelectedComboMemberSource =>
        SelectedCombo?.Kind == LimitsKind.Value ? ValueMemberSource : UsageMemberSource;

    partial void OnSelectedComboChanged(ComboVm? value)
        => OnPropertyChanged(nameof(SelectedComboMemberSource));

    public void AddCombo()
    {
        var name = (NewComboName ?? "").Trim();
        if (name.Length == 0) { Status = "✗ combo name must not be empty"; return; }
        var kind = NewComboIsUsage ? LimitsKind.Usage : LimitsKind.Value;
        // AddGroup silently REPLACES a same-name group of the same kind (wiping its members); reject the
        // collision here so an accidental re-add doesn't blow away an existing combo's contents.
        if (Combos.Any(c => c.Kind == kind && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        { Status = $"✗ a {kind} combo named \"{name}\" already exists"; return; }
        var (ok, msg) = _svc.AddGroup(kind, name, Array.Empty<string>());
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
        {
            NewComboName = "";
            ReloadCombos();
            SelectedCombo = Combos.FirstOrDefault(c => c.Name == name && c.Kind == kind);
            _onDictionaryChanged();
        }
    }

    public void RemoveSelectedCombo()
    {
        if (SelectedCombo is not { } c) return;
        if (!_confirm($"Remove the {c.Kind} combo \"{c.Name}\"?")) return;
        var (ok, msg) = _svc.RemoveGroup(c.Kind, c.Name);
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok) { ReloadCombos(); _onDictionaryChanged(); }
    }

    /// <summary>Persist the selected combo's edited member list (called from the chip control's Changed).</summary>
    public void SaveSelectedComboMembers()
    {
        if (SelectedCombo is not { } c) return;
        var (ok, msg) = _svc.SetGroupMembers(c.Kind, c.Name, c.Members.ToList());
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (!ok) return;
        _onDictionaryChanged();
        // Warn (don't block) when a member isn't in the base list — the lint rule that catches this is
        // skipped entirely when the base file is empty, so this is the only feedback in that case.
        var src = SelectedComboMemberSource;
        if (src.Count == 0) return;
        var unknown = c.Members.Where(m => !src.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            Status = $"⚠ saved — {string.Join(", ", unknown)} not in the base {c.Kind} list; the game can't resolve them";
    }

    /// <summary>Enter in-place rename on a combo (pencil / double-click).</summary>
    public void BeginEditCombo(ComboVm c)
    {
        SelectedCombo = c;
        c.IsEditing = true;
    }

    /// <summary>Commit an in-place combo rename: revert on empty/unchanged, else rename the group + reload.</summary>
    public void CommitComboEdit(ComboVm c)
    {
        if (!c.IsEditing) return;
        c.IsEditing = false;
        var newName = (c.Name ?? "").Trim();
        if (newName.Length == 0 || string.Equals(newName, c.OriginalName, StringComparison.Ordinal))
        {
            c.Name = c.OriginalName;
            return;
        }
        var (ok, msg) = _svc.RenameGroup(c.Kind, c.OriginalName, newName);
        Status = (ok ? "✓ " : "✗ ") + msg;
        if (ok) { ReloadCombos(); _onDictionaryChanged(); }
        else c.Name = c.OriginalName;
    }

    /// <summary>Abandon an in-place combo rename (Esc / ✗) — restore the persisted name.</summary>
    public void CancelComboEdit(ComboVm c)
    {
        c.Name = c.OriginalName;
        c.IsEditing = false;
    }
}

/// <summary>One named combo (cfglimitsdefinitionuser.xml group) with an editable member list + an in-place
/// rename state (<see cref="IsEditing"/>), matching the base-dictionary entry rows.</summary>
public sealed partial class ComboVm : ObservableObject
{
    public ComboVm(LimitsKind kind, string name, System.Collections.Generic.IReadOnlyList<string> members)
    {
        Kind = kind;
        _name = name;
        OriginalName = name;
        foreach (var m in members) Members.Add(m);
    }

    public LimitsKind Kind { get; }
    /// <summary>The name as last persisted (the rename source key).</summary>
    public string OriginalName { get; }
    public string KindLabel => Kind == LimitsKind.Value ? "value" : "usage";
    public ObservableCollection<string> Members { get; } = new();

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEditing;

    public string Display => $"{Name}  ({KindLabel})";
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Display));
}
