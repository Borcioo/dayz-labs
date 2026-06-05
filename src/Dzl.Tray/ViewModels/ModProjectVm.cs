using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Projects;

namespace Dzl.Tray.ViewModels;

/// <summary>Card view-model for a mod project on the My Mods page: the immutable project facts plus a
/// live <see cref="Git"/> summary filled in asynchronously (so shelling out to git never blocks the UI).
/// Mirrors <see cref="ModProject"/>'s Name/Path/Linked so existing card bindings keep working.</summary>
public sealed partial class ModProjectVm : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    public bool Linked { get; }

    /// <summary>Short git summary, e.g. "main • clean", "main • dirty ↑1", "no repo", "main • clean (local)".</summary>
    [ObservableProperty] private string _git = "…";

    public ModProjectVm(ModProject p)
    {
        Name = p.Name;
        Path = p.Path;
        Linked = p.Linked;
    }
}
