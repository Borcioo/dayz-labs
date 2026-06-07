using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.ViewModels;

/// <summary>Editable grid row for one types.xml entry. Holds the original <see cref="TypeEntry"/> so fields
/// not surfaced in the grid (flags, tags, quantmin/max) survive a save; <see cref="ToEntry"/> merges the
/// edited fields back onto it (carrying its <see cref="TypeEntry.SourceFile"/> so SaveAll routes it to the
/// right file). Usage/value (tiers) are edited as comma-separated strings. Also carries the CE origin/source
/// of the file it came from (for the source pill + filter) and a per-row lint summary.</summary>
public sealed partial class TypeRowVm : ObservableObject
{
    private readonly TypeEntry _original;

    [ObservableProperty] private string _name;
    [ObservableProperty] private int _nominal;
    [ObservableProperty] private int _min;
    [ObservableProperty] private int _lifetime;
    [ObservableProperty] private int _restock;
    [ObservableProperty] private int _cost;
    [ObservableProperty] private string _category;
    [ObservableProperty] private string _usage;   // comma-joined
    [ObservableProperty] private string _value;    // tiers, comma-joined

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
        _category = e.Category;
        _usage = string.Join(", ", e.Usage);
        _value = string.Join(", ", e.Value);
        SourceFile = e.SourceFile;
        Origin = origin;
        ModSource = modSource;
    }

    private static List<string> Split(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public TypeEntry ToEntry() => _original with
    {
        Name = Name.Trim(),
        Nominal = Nominal,
        Min = Min,
        Lifetime = Lifetime,
        Restock = Restock,
        Cost = Cost,
        Category = Category.Trim(),
        Usage = Split(Usage),
        Value = Split(Value),
        SourceFile = SourceFile,
    };
}
