using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.ViewModels;

/// <summary>
/// Backs the Economy "Types" tab (types.xml editor): the filterable master grid, the master–detail
/// selection, checkbox-based batch operations, snapshot undo/redo, debounced lint, and the
/// dictionary (cfglimitsdefinition.xml) suggestion lists. Extracted from <see cref="MainViewModel"/>
/// (which still owns the other CE tab VMs); exposed there as <c>TypesEditor</c>. All file I/O goes
/// through <see cref="TypesService"/> / <see cref="DictionaryService"/> (never throw; snapshot
/// backups before writes).
/// </summary>
public sealed partial class TypesEditorVm : ObservableObject, IDisposable
{
    private readonly string _configPath;
    // Reloads MainViewModel's Dictionaries tab after a free-add from the Types detail panel,
    // so both views of cfglimitsdefinition.xml stay in sync. Null-safe: not set in tests.
    private readonly Action? _onDictionariesChanged;
    private bool _disposed;

    public TypesEditorVm(string configPath, Action? onDictionariesChanged = null)
    {
        _configPath = configPath;
        _onDictionariesChanged = onDictionariesChanged;
        TypesView = CollectionViewSource.GetDefaultView(Types);
        TypesView.Filter = FilterType;
    }

    /// <summary>Editable rows for the active mission's types.xml.</summary>
    public ObservableCollection<TypeRowVm> Types { get; } = new();

    /// <summary>Filtered/sortable view over <see cref="Types"/> (drives the DataGrid).</summary>
    public ICollectionView TypesView { get; }

    /// <summary>Distinct categories (+ "" = all) for the category filter combo.</summary>
    public ObservableCollection<string> TypeCategories { get; } = new();

    [ObservableProperty] private string _typesFilter = "";
    [ObservableProperty] private string _typesCategoryFilter = "";
    [ObservableProperty] private string _typesStatus = "";

    /// <summary>Source-origin filter: "" = all, "Vanilla" / "Mod" / "Custom".</summary>
    [ObservableProperty] private string _typesSourceFilter = "";

    /// <summary>Main file selector at the TOP of the tab: the absolute path of the single CE file to scope
    /// the grid to, or "" = All files (union). Wired into <see cref="FilterType"/>.</summary>
    [ObservableProperty] private string _typesFileFilter = "";

    partial void OnTypesFileFilterChanged(string value) => RefreshTypesView();

    /// <summary>Options for the main file selector: (Label, Path). Index 0 is always ("All files (union)", "").
    /// Rebuilt on load from the distinct source files present in the loaded set.</summary>
    public ObservableCollection<FileFilterOption> TypesFileOptions { get; } = new();

    /// <summary>One entry in the top-of-tab file selector. <see cref="Path"/> = "" means "All files".</summary>
    public sealed record FileFilterOption(string Label, string Path);

    private void RefreshTypesFileOptions()
    {
        var keep = TypesFileFilter;
        TypesFileOptions.Clear();
        TypesFileOptions.Add(new FileFilterOption($"All files ({Types.Select(t => t.SourceFile).Distinct().Count()})", ""));
        foreach (var (name, path) in TypesSourceFiles())
        {
            var origin = _originByFile.TryGetValue(path, out var o) ? OriginUi.Label(o) : "Custom";
            TypesFileOptions.Add(new FileFilterOption($"{name}  ·  {origin}", path));
        }
        // Keep the prior selection if it still exists; else fall back to All.
        if (TypesFileOptions.All(opt => !string.Equals(opt.Path, keep, StringComparison.OrdinalIgnoreCase)))
            TypesFileFilter = "";
    }

    // Advanced filters — all substring/contains; blank = ignored.
    [ObservableProperty] private string _typesUsageFilter = "";
    [ObservableProperty] private string _typesValueFilter = "";
    [ObservableProperty] private string _typesTagFilter = "";
    /// <summary>Flag-presence filter: "" = any, else cargo/hoarder/map/player/crafted/deloot (row must have it set).</summary>
    [ObservableProperty] private string _typesFlagFilter = "";
    /// <summary>Nominal range filters as raw text (parsed leniently; blank/non-numeric = ignored).</summary>
    [ObservableProperty] private string _typesNominalMin = "";
    [ObservableProperty] private string _typesNominalMax = "";

    partial void OnTypesUsageFilterChanged(string value) => RefreshTypesView();
    partial void OnTypesValueFilterChanged(string value) => RefreshTypesView();
    partial void OnTypesTagFilterChanged(string value) => RefreshTypesView();
    partial void OnTypesFlagFilterChanged(string value) => RefreshTypesView();
    partial void OnTypesNominalMinChanged(string value) => RefreshTypesView();
    partial void OnTypesNominalMaxChanged(string value) => RefreshTypesView();

    /// <summary>Reset every Economy filter to its empty (show-all) state.</summary>
    public void ClearTypeFilters()
    {
        TypesFilter = ""; TypesCategoryFilter = ""; TypesSourceFilter = "";
        TypesFileFilter = "";
        TypesUsageFilter = ""; TypesValueFilter = ""; TypesTagFilter = "";
        TypesFlagFilter = ""; TypesNominalMin = ""; TypesNominalMax = "";
    }

    partial void OnTypesFilterChanged(string value) => RefreshTypesView();
    partial void OnTypesCategoryFilterChanged(string value) => RefreshTypesView();
    partial void OnTypesSourceFilterChanged(string value) => RefreshTypesView();

    /// <summary>Re-run the filter predicate and update the "showing X / N" count together, so every
    /// filter change keeps the count label in sync.</summary>
    private void RefreshTypesView()
    {
        TypesView.Refresh();
        OnPropertyChanged(nameof(TypesCountLabel));
        OnPropertyChanged(nameof(AllFilteredChecked));   // the filtered set (and thus tri-state) changed
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FiltersButtonLabel));
    }

    /// <summary>How many filters are currently non-empty — shown on the collapsed Filters button so
    /// active filtering is visible even when the filter panel is hidden.</summary>
    public int ActiveFilterCount => new[]
    {
        TypesFilter, TypesCategoryFilter, TypesSourceFilter, TypesFileFilter,
        TypesUsageFilter, TypesValueFilter, TypesTagFilter, TypesFlagFilter,
        TypesNominalMin, TypesNominalMax,
    }.Count(s => !string.IsNullOrEmpty(s));

    public string FiltersButtonLabel => ActiveFilterCount == 0 ? "Filters" : $"Filters ({ActiveFilterCount})";

    /// <summary>Path of the active mission's types.xml (or a hint), shown on the Economy page.</summary>
    public string TypesFile => new TypesService(_configPath).TypesFile() ?? "(no types.xml — pick/scaffold a server mission)";

    public bool HasTypes => new TypesService(_configPath).TypesFile() is not null;

    // Valid usage/value/tag/category names backing the editor dropdowns.
    /// <summary>Valid usage names from cfglimitsdefinition.xml (selectable in the Usage editor; free-text still allowed).</summary>
    public ObservableCollection<string> LimitsUsage { get; } = new();
    /// <summary>Valid value/tier names (selectable in the Tiers editor).</summary>
    public ObservableCollection<string> LimitsValue { get; } = new();
    /// <summary>Valid category names (selectable in the Category editor).</summary>
    public ObservableCollection<string> LimitsCategory { get; } = new();
    /// <summary>Valid tag names from cfglimitsdefinition.xml (selectable in the Tag editor).</summary>
    public ObservableCollection<string> LimitsTag { get; } = new();

    /// <summary>The row shown in the master–detail edit panel (the grid's primary selected item).
    /// Null = no selection → panel shows its "select a type" placeholder.</summary>
    [ObservableProperty] private TypeRowVm? _selectedType;

    /// <summary>All currently selected grid rows (multi-select), pushed from the grid's SelectedItems by
    /// the code-behind. Drives batch operations. Not an ObservableCollection — replaced wholesale per
    /// selection change.</summary>
    public System.Collections.Generic.IReadOnlyList<TypeRowVm> SelectedTypes { get; private set; }
        = System.Array.Empty<TypeRowVm>();

    /// <summary>Count of rows passing the current filter vs. the total loaded — drives "showing X / N".</summary>
    public string TypesCountLabel
    {
        get
        {
            var shown = TypesView.Cast<object>().Count();
            return $"showing {shown} / {Types.Count}";
        }
    }

    /// <summary>Called by the code-behind when the grid selection changes; updates the detail panel
    /// target and the batch selection.</summary>
    public void SetSelectedTypes(System.Collections.Generic.IReadOnlyList<TypeRowVm> rows, TypeRowVm? primary)
    {
        SelectedTypes = rows;
        SelectedType = primary;
        OnPropertyChanged(nameof(SelectedTypeCount));
        OnPropertyChanged(nameof(HasSelection));
    }

    public int SelectedTypeCount => SelectedTypes.Count;
    public bool HasSelection => SelectedTypes.Count > 0;

    // Selection-for-edit (grid SelectedItem → detail form) and selection-for-batch (checkboxes → many
    // rows) are SEPARATE concepts. Batch ops act on the CHECKED rows below, not the grid's SelectedItems.

    /// <summary>Rows currently checked (the batch target), among the FULL loaded set.</summary>
    public IReadOnlyList<TypeRowVm> CheckedTypes => Types.Where(t => t.IsSelected).ToList();

    /// <summary>Count of checked rows — drives the "on N checked" batch-bar label.</summary>
    public int CheckedTypeCount => Types.Count(t => t.IsSelected);

    /// <summary>True when at least one row is checked (gates the batch buttons / select-all tri-state).</summary>
    public bool HasChecked => Types.Any(t => t.IsSelected);

    /// <summary>Header "select all" tri-state over the rows currently passing the filter:
    /// true = all filtered rows checked, false = none, null = mixed. Setting it (from the header
    /// CheckBox) checks/unchecks every filtered row.</summary>
    public bool? AllFilteredChecked
    {
        get
        {
            var filtered = TypesView.Cast<TypeRowVm>().ToList();
            if (filtered.Count == 0) return false;
            var n = filtered.Count(t => t.IsSelected);
            if (n == 0) return false;
            if (n == filtered.Count) return true;
            return null;   // mixed
        }
        set
        {
            // The header CheckBox cycles true → null → false. When everything is checked the
            // getter snaps the visual back to true, so a null click must mean UNCHECK — treating
            // null as "check all" made it impossible to ever deselect a fully-checked set.
            var want = value == true;
            _suppressSelectionNotify = true;
            try { foreach (var t in TypesView.Cast<TypeRowVm>()) t.IsSelected = want; }
            finally { _suppressSelectionNotify = false; }
            NotifyCheckedChanged();
        }
    }

    private bool _suppressSelectionNotify;

    /// <summary>Wired to every row's <see cref="TypeRowVm.SelectionToggled"/>; refreshes the checked
    /// counters + select-all tri-state when a single row's checkbox flips.</summary>
    private void OnRowSelectionToggled()
    {
        if (_suppressSelectionNotify) return;
        NotifyCheckedChanged();
    }

    private void NotifyCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedTypes));
        OnPropertyChanged(nameof(CheckedTypeCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AllFilteredChecked));
        OnPropertyChanged(nameof(BatchMode));
    }

    /// <summary>True when 2 or more rows are checked — switches the right panel from the single-row
    /// edit form to the batch operations panel.</summary>
    public bool BatchMode => CheckedTypeCount >= 2;

    // Lint summary across the whole loaded set.
    [ObservableProperty] private string _typesLintSummary = "";
    /// <summary>True when the last lint pass found at least one warning or error; drives the lint-summary
    /// TextBlock's Visibility so it is hidden when there are no findings.</summary>
    [ObservableProperty] private bool _hasLintFindings;

    /// <summary>Maps each loaded source file → its CE origin, so undo/redo snapshots (which carry only
    /// <see cref="TypeEntry.SourceFile"/>) can re-derive the right origin pill.</summary>
    private readonly Dictionary<string, CeOrigin> _originByFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CE limits loaded on <see cref="LoadTypes"/>; cached so <see cref="RefreshTypesLint"/> can
    /// re-run in-memory without re-reading cfglimitsdefinition.xml on every keystroke.</summary>
    private Dzl.Core.Economy.LimitsDef _limits = Dzl.Core.Economy.LimitsDef.Empty;

    /// <summary>Distinct source files in the loaded set (file name → absolute path) for the Add-type target picker.
    /// The primary/vanilla file is always index 0 so the dialog defaults to it; row source files follow.</summary>
    public IReadOnlyList<(string Name, string Path)> TypesSourceFiles()
    {
        var svc = new TypesService(_configPath);
        var primary = svc.TypesFile();
        var list = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Primary first so SelectedIndex=0 always picks the primary/vanilla file.
        var ordered = (primary is null ? Array.Empty<string>() : new[] { primary })
                      .Concat(svc.Rows().Select(r => r.Entry.SourceFile))
                      .Where(p => !string.IsNullOrEmpty(p));
        foreach (var f in ordered)
            if (seen.Add(f)) list.Add((System.IO.Path.GetFileName(f), f));
        return list;
    }

    private TypeRowVm MakeRow(TypeRow r) => Track(new TypeRowVm(r));

    /// <summary>Subscribe a row's checkbox toggle to the VM's checked-counter refresh and return it
    /// (so it can be used inline in Add()). Rows are GC'd with the VM, so we don't bother unsubscribing.</summary>
    private TypeRowVm Track(TypeRowVm row)
    {
        row.SelectionToggled += OnRowSelectionToggled;
        return row;
    }

    private bool FilterType(object o)
    {
        if (o is not TypeRowVm t) return true;
        // A CollectionView filter predicate runs during collection mutation (e.g. while LoadTypes adds
        // rows) — at which point a bound filter ComboBox may have written null into one of these
        // properties (WPF clears SelectedItem when TypeCategories is repopulated). Guard with
        // IsNullOrEmpty, never `.Length`, so the predicate can't NRE on transient null state.
        if (!string.IsNullOrEmpty(TypesFilter) && !t.Name.Contains(TypesFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(TypesCategoryFilter) && !string.Equals(t.Category, TypesCategoryFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(TypesSourceFilter) && !string.Equals(OriginUi.Label(t.Origin), TypesSourceFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(TypesFileFilter) && !string.Equals(t.SourceFile, TypesFileFilter, StringComparison.OrdinalIgnoreCase)) return false;

        // List filters: a row passes if any of its values contains the filter substring.
        if (!string.IsNullOrEmpty(TypesUsageFilter) && !t.Usage.Any(u => u.Contains(TypesUsageFilter, StringComparison.OrdinalIgnoreCase))) return false;
        if (!string.IsNullOrEmpty(TypesValueFilter) && !t.Value.Any(v => v.Contains(TypesValueFilter, StringComparison.OrdinalIgnoreCase))) return false;
        if (!string.IsNullOrEmpty(TypesTagFilter) && !t.Tag.Any(g => g.Contains(TypesTagFilter, StringComparison.OrdinalIgnoreCase))) return false;

        // Flag presence: row must have the chosen flag set.
        if (!string.IsNullOrEmpty(TypesFlagFilter))
        {
            var hasFlag = TypesFlagFilter switch
            {
                "cargo" => t.CountInCargo,
                "hoarder" => t.CountInHoarder,
                "map" => t.CountInMap,
                "player" => t.CountInPlayer,
                "crafted" => t.Crafted,
                "deloot" => t.Deloot,
                _ => true,
            };
            if (!hasFlag) return false;
        }

        // Nominal range (lenient parse; ignored when blank/non-numeric).
        if (int.TryParse(TypesNominalMin, out var nmin) && t.Nominal < nmin) return false;
        if (int.TryParse(TypesNominalMax, out var nmax) && t.Nominal > nmax) return false;

        return true;
    }

    // Undo/redo: in-session, snapshot-based history (whole entry-list snapshots).
    private const int UndoCap = 50;
    private readonly List<List<Dzl.Core.Economy.TypeEntry>> _typesUndo = new();
    private readonly List<List<Dzl.Core.Economy.TypeEntry>> _typesRedo = new();
    private List<Dzl.Core.Economy.TypeEntry>? _pendingEdit;   // captured at cell BeginEdit

    public bool CanUndoTypes => _typesUndo.Count > 0;
    public bool CanRedoTypes => _typesRedo.Count > 0;

    /// <summary>True when the in-memory set has been mutated since the last load/save. Set on every
    /// undo-snapshot push; cleared by <see cref="LoadTypes"/> (which also runs after a successful
    /// save). Conservative on purpose: undoing back to the original state stays "dirty".</summary>
    [ObservableProperty] private bool _hasUnsavedChanges;

    private List<Dzl.Core.Economy.TypeEntry> CaptureTypes() => Types.Select(t => t.ToEntry()).ToList();

    private void PushTypeUndo()
    {
        _typesUndo.Add(CaptureTypes());
        if (_typesUndo.Count > UndoCap) _typesUndo.RemoveAt(0);
        _typesRedo.Clear();
        HasUnsavedChanges = true;
        AfterTypeHistoryChange();
    }

    private void AfterTypeHistoryChange()
    {
        OnPropertyChanged(nameof(CanUndoTypes));
        OnPropertyChanged(nameof(CanRedoTypes));
        UndoTypesCommand.NotifyCanExecuteChanged();
        RedoTypesCommand.NotifyCanExecuteChanged();
    }

    private void RestoreTypeSnapshot(List<Dzl.Core.Economy.TypeEntry> snap)
    {
        Types.Clear();
        foreach (var e in snap)
        {
            var origin = _originByFile.TryGetValue(e.SourceFile ?? "", out var o) ? o : CeOrigin.Custom;
            Types.Add(Track(new TypeRowVm(e, origin)));
        }
        RefreshTypeCategories();
        RefreshTypesLint();
        RefreshTypesView();
        SetSelectedTypes(System.Array.Empty<TypeRowVm>(), null);
        NotifyCheckedChanged();
    }

    /// <summary>Capture the pre-edit state when a grid cell starts editing (committed on edit-commit).</summary>
    public void BeginTypeEdit() => _pendingEdit = CaptureTypes();

    public void CommitTypeEdit()
    {
        if (_pendingEdit is null) return;
        _typesUndo.Add(_pendingEdit);
        if (_typesUndo.Count > UndoCap) _typesUndo.RemoveAt(0);
        _typesRedo.Clear();
        _pendingEdit = null;
        HasUnsavedChanges = true;
        AfterTypeHistoryChange();
        ScheduleLint();   // reflect in-memory edits after a cell commit (debounced for rapid edits)
    }

    public void CancelTypeEdit() => _pendingEdit = null;

    [RelayCommand(CanExecute = nameof(CanUndoTypes))]
    private void UndoTypes()
    {
        if (_typesUndo.Count == 0) return;
        var prev = _typesUndo[^1]; _typesUndo.RemoveAt(_typesUndo.Count - 1);
        _typesRedo.Add(CaptureTypes());
        RestoreTypeSnapshot(prev);
        AfterTypeHistoryChange();
        TypesStatus = "undo";
    }

    [RelayCommand(CanExecute = nameof(CanRedoTypes))]
    private void RedoTypes()
    {
        if (_typesRedo.Count == 0) return;
        var next = _typesRedo[^1]; _typesRedo.RemoveAt(_typesRedo.Count - 1);
        _typesUndo.Add(CaptureTypes());
        RestoreTypeSnapshot(next);
        AfterTypeHistoryChange();
        TypesStatus = "redo";
    }

    private void RefreshTypeCategories()
    {
        var sel = TypesCategoryFilter;
        TypeCategories.Clear();
        TypeCategories.Add("");   // all
        foreach (var c in Types.Select(t => t.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => c))
            TypeCategories.Add(c);
        if (!TypeCategories.Contains(sel)) TypesCategoryFilter = "";
    }

    /// <summary>(Re)load types from the active mission's Types files (multi-file). Clears the undo history,
    /// reloads the limits dropdowns, and recomputes lint.</summary>
    public void LoadTypes()
    {
        var svc = new TypesService(_configPath);
        Types.Clear();
        _originByFile.Clear();
        foreach (var r in svc.Rows())
        {
            _originByFile[r.Entry.SourceFile] = r.Origin;
            Types.Add(MakeRow(r));
        }
        RefreshTypeCategories();
        RefreshTypesFileOptions();
        RefreshLimits(svc);
        _typesUndo.Clear(); _typesRedo.Clear(); _pendingEdit = null;
        HasUnsavedChanges = false;
        AfterTypeHistoryChange();
        OnPropertyChanged(nameof(TypesFile));
        OnPropertyChanged(nameof(HasTypes));
        RefreshTypesLint();
        RefreshTypesView();
        TypesStatus = $"{Types.Count} types";
    }

    private void RefreshLimits(TypesService svc)
    {
        _limits = svc.Limits();
        FillSet(LimitsUsage, _limits.Usage);
        FillSet(LimitsValue, _limits.Value);
        FillSet(LimitsCategory, _limits.Category);
        FillSet(LimitsTag, _limits.Tag);

        static void FillSet(ObservableCollection<string> target, IReadOnlySet<string> src)
        {
            target.Clear();
            foreach (var v in src.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) target.Add(v);
        }
    }

    /// <summary>Re-read cfglimitsdefinition.xml and refresh the suggestion lists + re-lint.
    /// Invoked (via MainViewModel's Dictionaries wiring) after every dictionary edit so the Types
    /// tab stays in sync without a full reload.</summary>
    public void RefreshLimitsFromDisk()
    {
        _limits = new DictionaryService(_configPath).Load();
        void Fill(ObservableCollection<string> target, IReadOnlySet<string> src)
        {
            target.Clear();
            foreach (var v in src.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) target.Add(v);
        }
        Fill(LimitsUsage, _limits.Usage);
        Fill(LimitsValue, _limits.Value);
        Fill(LimitsCategory, _limits.Category);
        Fill(LimitsTag, _limits.Tag);
        RefreshTypesLint();
    }

    /// <summary>Count loaded types referencing <paramref name="name"/> in the list matching <paramref name="kind"/>
    /// (Category compares the single Category field; Usage/Value/Tag scan the list). Drives the remove-in-use warning.</summary>
    public int CountTypesUsing(Dzl.Core.Economy.LimitsKind kind, string name)
    {
        bool Has(TypeRowVm t) => kind switch
        {
            Dzl.Core.Economy.LimitsKind.Category => string.Equals(t.Category, name, StringComparison.OrdinalIgnoreCase),
            Dzl.Core.Economy.LimitsKind.Usage => t.Usage.Any(u => string.Equals(u, name, StringComparison.OrdinalIgnoreCase)),
            Dzl.Core.Economy.LimitsKind.Value => t.Value.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)),
            Dzl.Core.Economy.LimitsKind.Tag => t.Tag.Any(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
        return Types.Count(Has);
    }

    /// <summary>Add a value to the live dictionary for a given chip kind (the Types editor's free-add →
    /// dictionary affordance). Returns the service status; refreshes suggestions + lint on success.</summary>
    public (bool ok, string msg) AddToDictionary(Dzl.Core.Economy.LimitsKind kind, string name)
    {
        var r = new DictionaryService(_configPath).AddName(kind, name);
        if (r.ok)
        {
            RefreshLimitsFromDisk();
            _onDictionariesChanged?.Invoke();
        }
        return r;
    }

    /// <summary>True when <paramref name="name"/> is NOT a known value of the given chip kind (so the
    /// Types detail panel can offer to register it in the dictionary).</summary>
    public bool IsUnknownLimit(Dzl.Core.Economy.LimitsKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var set = kind switch
        {
            Dzl.Core.Economy.LimitsKind.Usage => _limits.Usage,
            Dzl.Core.Economy.LimitsKind.Value => _limits.Value,
            Dzl.Core.Economy.LimitsKind.Tag => _limits.Tag,
            Dzl.Core.Economy.LimitsKind.Category => _limits.Category,
            _ => (IReadOnlySet<string>)new HashSet<string>(),
        };
        return !set.Contains(name.Trim());
    }

    // Debounced lint: RefreshTypesLint walks ~2000 rows; running it per keystroke freezes the editor. Rapid-edit paths
    // (detail typing, steppers, batch) call ScheduleLint() which restarts a ~300 ms timer so bursts
    // coalesce into ONE lint pass. Load / undo / redo still lint immediately (RefreshTypesLint directly).
    private DispatcherTimer? _lintTimer;

    private void ScheduleLint()
    {
        _lintTimer ??= CreateLintTimer();
        _lintTimer.Stop();
        _lintTimer.Start();
    }

    private DispatcherTimer CreateLintTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) => { t.Stop(); if (!_disposed) RefreshTypesLint(); };
        return t;
    }

    /// <summary>Recompute lint over the CURRENT in-memory rows (reflects unsaved edits immediately).
    /// Uses <see cref="_limits"/> cached at load time so no disk read is needed per call.
    /// Called on load, undo/redo, add, remove, batch-apply, and cell-commit.</summary>
    private void RefreshTypesLint()
    {
        IReadOnlyList<Dzl.Core.Economy.Lint.LintFinding> findings;
        try
        {
            var entries = Types.Select(t => t.ToEntry());
            findings = new Dzl.Core.Economy.Lint.LintEngine().Run(
                new Dzl.Core.Economy.CeFileSet(entries), _limits);
        }
        catch { findings = Array.Empty<Dzl.Core.Economy.Lint.LintFinding>(); }

        var byName = findings
            .Where(f => !string.IsNullOrEmpty(f.EntryName))
            .GroupBy(f => f.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in Types)
        {
            if (byName.TryGetValue(row.Name, out var list))
            {
                row.LintCount = list.Count;
                row.LintTooltip = string.Join("\n", list.Select(f => $"[{f.Severity}] {f.Message}"));
            }
            else
            {
                row.LintCount = 0;
                row.LintTooltip = "";
            }
        }

        var errors = findings.Count(f => f.Severity == Dzl.Core.Economy.Lint.LintSeverity.Error);
        var warnings = findings.Count(f => f.Severity == Dzl.Core.Economy.Lint.LintSeverity.Warning);
        HasLintFindings = errors > 0 || warnings > 0;
        TypesLintSummary = HasLintFindings
            ? $"{warnings} warning(s) / {errors} error(s)"
            : "";
    }

    /// <summary>Persist the full edited set (snapshots a versioned backup first).</summary>
    public void SaveTypes()
    {
        var r = new TypesService(_configPath).SaveAll(Types.Select(t => t.ToEntry()).ToList());
        TypesStatus = (r.Ok ? "✓ " : "✗ ") + r.Message;
        if (r.Ok) LoadTypes();
    }

    /// <summary>Add a new in-memory type. <paramref name="targetFile"/> (a resolved CE file path; empty →
    /// the primary/vanilla file) is stamped onto the row's <see cref="TypeEntry.SourceFile"/> so SaveAll
    /// routes it to that file.</summary>
    public void AddType(string name, string? targetFile = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        PushTypeUndo();
        var file = string.IsNullOrEmpty(targetFile)
            ? (new TypesService(_configPath).TypesFile() ?? "")
            : targetFile;
        var origin = _originByFile.TryGetValue(file, out var o) ? o : CeOrigin.Custom;
        var row = Track(new TypeRowVm(
            new Dzl.Core.Economy.TypeEntry { Name = name.Trim(), Nominal = 1, Min = 0, Lifetime = 3888000, Cost = 100, SourceFile = file },
            origin));
        Types.Add(row);
        RefreshTypesView();
        RefreshTypesLint();   // new row may introduce or resolve lint findings
        TypesStatus = $"added {name} → {row.FileName} (unsaved)";
    }

    public void RemoveTypes(IReadOnlyList<TypeRowVm> rows)
    {
        if (rows.Count == 0) return;
        PushTypeUndo();
        foreach (var r in rows) Types.Remove(r);
        RefreshTypesView();
        RefreshTypesLint();   // removed rows may have carried lint findings
        NotifyCheckedChanged();
        TypesStatus = $"removed {rows.Count} (unsaved — Save to persist)";
    }

    /// <summary>Batch-apply a numeric field across selected rows: set to <paramref name="value"/>, or
    /// multiply by it (when <paramref name="multiply"/>). Covers every numeric field. In-memory only
    /// until Save. One undo step.</summary>
    public void BatchApply(IReadOnlyList<TypeRowVm> rows, string field, double value, bool multiply)
    {
        if (rows.Count == 0) return;
        PushTypeUndo();
        foreach (var t in rows)
        {
            switch (field)
            {
                case "nominal": t.Nominal = Apply(t.Nominal); break;
                case "min": t.Min = Apply(t.Min); break;
                case "quantmin": t.QuantMin = ApplyAllowNeg(t.QuantMin); break;
                case "quantmax": t.QuantMax = ApplyAllowNeg(t.QuantMax); break;
                case "lifetime": t.Lifetime = Apply(t.Lifetime); break;
                case "restock": t.Restock = Apply(t.Restock); break;
                case "cost": t.Cost = Apply(t.Cost); break;
            }
        }
        RefreshTypesLint();   // batch edits may cross lint thresholds (nominal/min rules)
        RefreshTypesView();
        TypesStatus = $"batch {(multiply ? "×" : "=")}{value} {field} on {rows.Count} (unsaved)";

        int Apply(int cur) => multiply ? Math.Max(0, (int)Math.Round(cur * value)) : Math.Max(0, (int)value);
        int ApplyAllowNeg(int cur) => multiply ? (int)Math.Round(cur * value) : (int)value;
    }

    /// <summary>Batch-divide a numeric field across selected rows. Guarded: <paramref name="value"/> = 0
    /// is a no-op (the UI also blocks it) — never throws, never zeroes the column by accident.
    /// Same rounding/clamping as <see cref="BatchApply"/>. One undo step.</summary>
    public void BatchDivide(IReadOnlyList<TypeRowVm> rows, string field, double value)
    {
        if (rows.Count == 0 || value == 0) return;
        PushTypeUndo();
        foreach (var t in rows)
        {
            switch (field)
            {
                case "nominal": t.Nominal = Div(t.Nominal); break;
                case "min": t.Min = Div(t.Min); break;
                case "quantmin": t.QuantMin = DivAllowNeg(t.QuantMin); break;
                case "quantmax": t.QuantMax = DivAllowNeg(t.QuantMax); break;
                case "lifetime": t.Lifetime = Div(t.Lifetime); break;
                case "restock": t.Restock = Div(t.Restock); break;
                case "cost": t.Cost = Div(t.Cost); break;
            }
        }
        RefreshTypesLint();
        RefreshTypesView();
        TypesStatus = $"batch ÷{value} {field} on {rows.Count} (unsaved)";

        int Div(int cur) => Math.Max(0, (int)Math.Round(cur / value));
        int DivAllowNeg(int cur) => (int)Math.Round(cur / value);
    }

    /// <summary>Batch flag op across selected rows. <paramref name="op"/> ∈ {set, clear, toggle}.
    /// <paramref name="flag"/> ∈ {cargo, hoarder, map, player, crafted, deloot}. One undo step.</summary>
    public void BatchFlag(IReadOnlyList<TypeRowVm> rows, string flag, string op)
    {
        if (rows.Count == 0) return;
        PushTypeUndo();
        foreach (var t in rows)
        {
            switch (flag)
            {
                case "cargo":   t.CountInCargo   = Next(t.CountInCargo);   break;
                case "hoarder": t.CountInHoarder = Next(t.CountInHoarder); break;
                case "map":     t.CountInMap     = Next(t.CountInMap);     break;
                case "player":  t.CountInPlayer  = Next(t.CountInPlayer);  break;
                case "crafted": t.Crafted        = Next(t.Crafted);        break;
                case "deloot":  t.Deloot         = Next(t.Deloot);         break;
            }
        }
        RefreshTypesLint();
        RefreshTypesView();
        TypesStatus = $"batch flag {op} {flag} on {rows.Count} (unsaved)";

        bool Next(bool cur) => op switch { "set" => true, "clear" => false, _ => !cur };
    }

    /// <summary>Batch add/remove a list value (usage/value/tag) across selected rows. One undo step.</summary>
    public void BatchList(IReadOnlyList<TypeRowVm> rows, string list, string value, bool add)
    {
        value = value.Trim();
        if (rows.Count == 0 || string.IsNullOrEmpty(value)) return;
        PushTypeUndo();
        foreach (var t in rows)
        {
            var col = list switch { "usage" => t.Usage, "value" => t.Value, "tag" => t.Tag, _ => null };
            if (col is null) continue;
            if (add)
            {
                if (!col.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase))) col.Add(value);
            }
            else
            {
                var hit = col.FirstOrDefault(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) col.Remove(hit);
            }
            t.NotifyListText();
        }
        RefreshTypesLint();
        RefreshTypesView();
        TypesStatus = $"batch {(add ? "add" : "remove")} {list}='{value}' on {rows.Count} (unsaved)";
    }

    /// <summary>Batch set the category across selected rows. One undo step.</summary>
    public void BatchCategory(IReadOnlyList<TypeRowVm> rows, string category)
    {
        if (rows.Count == 0) return;
        PushTypeUndo();
        foreach (var t in rows) t.Category = category.Trim();
        RefreshTypeCategories();
        RefreshTypesLint();
        RefreshTypesView();
        TypesStatus = $"batch category='{category}' on {rows.Count} (unsaved)";
    }

    /// <summary>Adjust one numeric field on one row by a delta (the detail panel's +/- steppers go through
    /// this so each click is its own undo step and re-lints). Field names match the detail panel.</summary>
    public void StepField(TypeRowVm row, string field, int delta)
    {
        PushTypeUndo();
        switch (field)
        {
            case "nominal":  row.Nominal  = Math.Max(0, row.Nominal + delta);  break;
            case "min":      row.Min      = Math.Max(0, row.Min + delta);      break;
            case "quantmin": row.QuantMin = row.QuantMin + delta;              break;
            case "quantmax": row.QuantMax = row.QuantMax + delta;              break;
            case "lifetime": row.Lifetime = Math.Max(0, row.Lifetime + delta); break;
            case "restock":  row.Restock  = Math.Max(0, row.Restock + delta);  break;
            case "cost":     row.Cost     = Math.Max(0, row.Cost + delta);     break;
        }
        ScheduleLint();
    }

    /// <summary>Duplicate a row under a new name into a target file (empty → the source row's file).
    /// Copies the FULL field set (numbers, flags, all three lists). New row lints immediately.</summary>
    public void DuplicateType(TypeRowVm src, string newName, string? targetFile = null)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        PushTypeUndo();
        var file = string.IsNullOrEmpty(targetFile) ? src.SourceFile : targetFile!;
        var origin = _originByFile.TryGetValue(file, out var o) ? o : src.Origin;
        var entry = src.ToEntry() with { Name = newName.Trim(), SourceFile = file };
        var row = Track(new TypeRowVm(entry, origin));
        Types.Add(row);
        RefreshTypesView();
        RefreshTypesLint();
        TypesStatus = $"duplicated {src.Name} → {newName} (unsaved)";
    }

    /// <summary>Duplicate every row in <paramref name="rows"/> (the batch-checked set). Each copy keeps
    /// the source row's full field set and file, under a collision-safe name (Name_Copy, Name_Copy2…).
    /// One undo step for the whole batch; copies start unchecked.</summary>
    public void DuplicateTypes(IReadOnlyList<TypeRowVm> rows)
    {
        if (rows.Count == 0) return;
        PushTypeUndo();
        var taken = new HashSet<string>(Types.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var src in rows)
        {
            var name = UniqueName(src.Name, taken);
            taken.Add(name);
            var entry = src.ToEntry() with { Name = name };
            Types.Add(Track(new TypeRowVm(entry, src.Origin)));
        }
        RefreshTypesView();
        RefreshTypesLint();
        TypesStatus = $"duplicated {rows.Count} (unsaved)";

        static string UniqueName(string baseName, HashSet<string> taken)
        {
            var name = baseName + "_Copy";
            var i = 2;
            while (taken.Contains(name)) name = $"{baseName}_Copy{i++}";
            return name;
        }
    }

    /// <summary>Snapshot the current state as one undo step before a detail-panel edit (numeric typing,
    /// category, flag toggle). Called by the code-behind when a detail field gains focus / a toggle flips.</summary>
    public void PushDetailEditUndo() => PushTypeUndo();

    /// <summary>Re-lint + refresh after a detail-panel / chip edit (no new undo step — used after a value
    /// that was already snapshotted, or for chip edits which snapshot themselves via PushDetailEditUndo).</summary>
    public void AfterDetailEdit()
    {
        SelectedType?.NotifyListText();
        ScheduleLint();
        RefreshTypeCategories();
    }

    public List<Dzl.Core.Economy.TypesBackupInfo> TypesBackups() => new TypesService(_configPath).Backups();

    public void RestoreTypes(string file)
    {
        var r = new TypesService(_configPath).Restore(file);
        TypesStatus = (r.Ok ? "✓ " : "✗ ") + r.Message;
        if (r.Ok) LoadTypes();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lintTimer?.Stop();
    }
}
