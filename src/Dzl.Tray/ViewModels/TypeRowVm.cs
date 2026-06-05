using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Economy;

namespace Dzl.Tray.ViewModels;

/// <summary>Editable grid row for one types.xml entry. Holds the original <see cref="TypeEntry"/> so fields
/// not surfaced in the grid (flags, tags, quantmin/max) survive a save; <see cref="ToEntry"/> merges the
/// edited fields back onto it. Usage/value (tiers) are edited as comma-separated strings.</summary>
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

    public TypeRowVm(TypeEntry e)
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
    };
}
