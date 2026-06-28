using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Mods;
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

    /// <summary>A pack = a folder whose subfolders are the actual mods (see <see cref="Children"/>); git and
    /// identity live at this (pack) level.</summary>
    public bool IsPack { get; }

    /// <summary>The inner mods of a pack (empty for a standalone mod).</summary>
    public IReadOnlyList<ModProjectVm> Children { get; }

    /// <summary>Header badge for a pack, e.g. "pack · 3 mods".</summary>
    public string PackSummary => IsPack ? $"pack · {Children.Count} mods" : "";

    /// <summary>Whether this pack's group is expanded on My Mods (two-way bound to the Expander). Packs default to
    /// COLLAPSED; the seed comes from persisted UI state (only explicitly-expanded packs are remembered), so the
    /// chosen state survives refreshes and app restarts.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Short git summary, e.g. "main • clean", "main • dirty ↑1", "no repo", "main • clean (local)".</summary>
    [ObservableProperty] private string _git = "…";

    /// <summary>Browsable URL of the project's git remote, or null when there's no remote (drives the
    /// "Open on GitHub" button — disabled when null).</summary>
    [ObservableProperty] private string? _repoUrl;

    public bool HasRepoUrl => !string.IsNullOrEmpty(RepoUrl);
    partial void OnRepoUrlChanged(string? value) => OnPropertyChanged(nameof(HasRepoUrl));

    // My Mods are always the uncompiled source — its compiled counterpart shows as "Build" in the Mods library.
    public string KindLabel => Dzl.Tray.ModKindUi.Label(ModKind.Source);
    public Brush KindBg => Dzl.Tray.ModKindUi.Bg(ModKind.Source);
    public Brush KindFg => Dzl.Tray.ModKindUi.Fg(ModKind.Source);

    public ModProjectVm(ModProject p, bool expanded = false)
    {
        Name = p.Name;
        Path = p.Path;
        Linked = p.Linked;
        IsPack = p.IsPack;
        IsExpanded = expanded;
        Children = p.Children.Select(c => new ModProjectVm(c)).ToList();
    }
}
