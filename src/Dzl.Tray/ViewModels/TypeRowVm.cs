using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.ViewModels;

/// <summary>Editable grid row for one types.xml entry. Holds the original <see cref="TypeEntry"/> so fields
/// not surfaced in the grid survive a save; <see cref="ToEntry"/> merges ALL edited fields back onto it
/// (carrying its <see cref="TypeEntry.SourceFile"/> so SaveAll routes it to the right file).
/// Usage/Value/Tag are held as <see cref="ObservableCollection{T}"/> of strings; the
/// <see cref="UsageText"/>/<see cref="ValueText"/>/<see cref="TagText"/> properties expose comma-joined
/// display strings that the grid columns bind to (read for display, write to update the collections).</summary>
public sealed partial class TypeRowVm : ObservableObject
{
    private readonly TypeEntry _original;

    // --- core numeric fields ---
    [ObservableProperty] private string _name;
    [ObservableProperty] private int _nominal;
    [ObservableProperty] private int _min;
    [ObservableProperty] private int _lifetime;
    [ObservableProperty] private int _restock;
    [ObservableProperty] private int _cost;

    // --- quant fields (new; -1 = not set, matching TypeEntry default) ---
    [ObservableProperty] private int _quantMin = -1;
    [ObservableProperty] private int _quantMax = -1;

    // --- category ---
    [ObservableProperty] private string _category;

    // --- flags (6 CE spawn flags) ---
    [ObservableProperty] private bool _countInCargo;
    [ObservableProperty] private bool _countInHoarder;
    [ObservableProperty] private bool _countInMap;
    [ObservableProperty] private bool _countInPlayer;
    [ObservableProperty] private bool _crafted;
    [ObservableProperty] private bool _deloot;

    // --- list fields (editable collections) ---
    /// <summary>Usage categories (e.g. "Military", "Civilian"). Bound as <see cref="UsageText"/> in the grid.</summary>
    public ObservableCollection<string> Usage { get; } = new();

    /// <summary>Value/tier names (e.g. "Tier1", "Tier2"). Bound as <see cref="ValueText"/> in the grid.</summary>
    public ObservableCollection<string> Value { get; } = new();

    /// <summary>Tags (e.g. "floor", "ground"). Bound as <see cref="TagText"/> in the grid.</summary>
    public ObservableCollection<string> Tag { get; } = new();

    // --- display/edit text proxies for the grid columns (comma-joined strings) ---
    // These are the properties that XAML binds to so existing grid columns still compile and show the right data.
    // Setting them parses the comma-separated input back into the respective ObservableCollection.

    /// <summary>Comma-joined display / edit string for <see cref="Usage"/>.
    /// XAML grid columns bind to this; setting it updates the <see cref="Usage"/> collection.</summary>
    public string UsageText
    {
        get => string.Join(", ", Usage);
        set
        {
            var parsed = Split(value);
            if (ListEquals(Usage, parsed)) return;
            Usage.Clear();
            foreach (var s in parsed) Usage.Add(s);
            OnPropertyChanged();
        }
    }

    /// <summary>Comma-joined display / edit string for <see cref="Value"/> (tiers).
    /// XAML grid columns bind to this; setting it updates the <see cref="Value"/> collection.</summary>
    public string ValueText
    {
        get => string.Join(", ", Value);
        set
        {
            var parsed = Split(value);
            if (ListEquals(Value, parsed)) return;
            Value.Clear();
            foreach (var s in parsed) Value.Add(s);
            OnPropertyChanged();
        }
    }

    /// <summary>Comma-joined display / edit string for <see cref="Tag"/>.
    /// XAML grid columns may bind to this for display.</summary>
    public string TagText
    {
        get => string.Join(", ", Tag);
        set
        {
            var parsed = Split(value);
            if (ListEquals(Tag, parsed)) return;
            Tag.Clear();
            foreach (var s in parsed) Tag.Add(s);
            OnPropertyChanged();
        }
    }

    // --- origin / source metadata ---

    /// <summary>Absolute path of the CE file this entry was read from / saves to.</summary>
    public string SourceFile { get; private set; }

    /// <summary>Origin of the source file (Vanilla / Mod / Custom) — drives the source pill + filter.</summary>
    public CeOrigin Origin { get; }

    /// <summary>Mod/folder this file belongs to (e.g. "vanilla", a mod name).
    /// Currently unbound/reserved — will feed per-mod-source chips in SP-CE1.</summary>
    public string ModSource { get; }

    /// <summary>Just the file name of <see cref="SourceFile"/>, shown in the File column.</summary>
    public string FileName => string.IsNullOrEmpty(SourceFile) ? "(new)" : Path.GetFileName(SourceFile);

    // --- origin pill (mirrors the mod-source pill used on the Mods/Servers pages) ---
    public string OriginLabel => OriginUi.Label(Origin);
    public Brush OriginBg => OriginUi.Bg(Origin);
    public Brush OriginFg => OriginUi.Fg(Origin);

    // --- lint summary (per-row; refreshed from MainViewModel after load/edit) ---
    [ObservableProperty] private int _lintCount;
    [ObservableProperty] private string _lintTooltip = "";

    public bool HasLint => LintCount > 0;
    public string LintGlyph => LintCount == 0 ? "" : $"⚠ {LintCount}";

    partial void OnLintCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasLint));
        OnPropertyChanged(nameof(LintGlyph));
    }

    // --- constructors ---

    /// <summary>Loaded row: carries the source file + origin from <see cref="TypeRow"/>.</summary>
    public TypeRowVm(TypeRow row) : this(row.Entry, row.Origin, row.ModSource) { }

    /// <summary>New/in-memory row with an explicit origin/source (e.g. from the Add-type dialog).</summary>
    public TypeRowVm(TypeEntry e, CeOrigin origin = CeOrigin.Custom, string modSource = "")
    {
        _original = e;
        _name = e.Name;
        _nominal = e.Nominal;
        _min = e.Min;
        _lifetime = e.Lifetime;
        _restock = e.Restock;
        _cost = e.Cost;
        _quantMin = e.QuantMin;
        _quantMax = e.QuantMax;
        _category = e.Category;

        // flags — use the entry's actual values (NOT defaulting CountInMap to true here;
        // a loaded entry already has its real flag values baked in via TypeEntry.Flags).
        _countInCargo = e.Flags.CountInCargo;
        _countInHoarder = e.Flags.CountInHoarder;
        _countInMap = e.Flags.CountInMap;
        _countInPlayer = e.Flags.CountInPlayer;
        _crafted = e.Flags.Crafted;
        _deloot = e.Flags.Deloot;

        // lists
        foreach (var s in e.Usage) Usage.Add(s);
        foreach (var s in e.Value) Value.Add(s);
        foreach (var s in e.Tag) Tag.Add(s);

        SourceFile = e.SourceFile;
        Origin = origin;
        ModSource = modSource;
    }

    // --- helpers ---

    private static List<string> Split(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool ListEquals(ObservableCollection<string> col, List<string> other)
    {
        if (col.Count != other.Count) return false;
        for (int i = 0; i < col.Count; i++)
            if (!string.Equals(col[i], other[i], StringComparison.Ordinal)) return false;
        return true;
    }

    // --- ToEntry: writes EVERY field back so round-trips are lossless ---

    public TypeEntry ToEntry() => _original with
    {
        Name = Name.Trim(),
        Nominal = Nominal,
        Min = Min,
        Lifetime = Lifetime,
        Restock = Restock,
        QuantMin = QuantMin,
        QuantMax = QuantMax,
        Cost = Cost,
        Category = Category.Trim(),
        Flags = new TypeFlags
        {
            CountInCargo = CountInCargo,
            CountInHoarder = CountInHoarder,
            CountInMap = CountInMap,
            CountInPlayer = CountInPlayer,
            Crafted = Crafted,
            Deloot = Deloot,
        },
        Usage = Usage.ToList(),
        Value = Value.ToList(),
        Tag = Tag.ToList(),
        SourceFile = SourceFile,
    };
}
