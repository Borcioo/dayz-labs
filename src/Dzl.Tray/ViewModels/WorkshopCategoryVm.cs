using CommunityToolkit.Mvvm.ComponentModel;

namespace Dzl.Tray.ViewModels;

/// <summary>A toggleable Workshop filter tag (Type / Mod-Type category). Raises <see cref="Toggled"/> when the
/// checkbox flips so the browser re-queries with the new requiredtags set.</summary>
public sealed partial class WorkshopCategoryVm : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _selected;

    public event Action? Toggled;
    partial void OnSelectedChanged(bool value) => Toggled?.Invoke();

    public WorkshopCategoryVm(string name) => Name = name;
}
