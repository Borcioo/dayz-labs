using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Mods;

namespace Dzl.Tray.ViewModels;

/// <summary>
/// One row in the mod checklist. <see cref="Enabled"/> and <see cref="Side"/> are
/// editable; when either changes the owning <see cref="MainViewModel"/> rebuilds the
/// argv preview and persists the active profile (via <see cref="Changed"/>).
/// </summary>
public sealed partial class ModRowVm : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _side = "both"; // both|server|client
    [ObservableProperty] private bool _missing;

    /// <summary>1-based load-order position, maintained by <see cref="MainViewModel"/>
    /// after every reorder/rescan so the grid's "#" column stays correct. Not persisted
    /// itself — order is implied by the position in the saved mod list.</summary>
    [ObservableProperty] private int _order;

    /// <summary>What this mod is, by where it lives (Source / Build / Workshop / steamcmd / External).</summary>
    [ObservableProperty] private ModKind _kind = ModKind.External;

    /// <summary>Badge text + colors for <see cref="Kind"/>.</summary>
    public string KindLabel => ModKindUi.Label(Kind);
    public Brush KindBg => ModKindUi.Bg(Kind);
    public Brush KindFg => ModKindUi.Fg(Kind);

    partial void OnKindChanged(ModKind value)
    {
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(KindBg));
        OnPropertyChanged(nameof(KindFg));
    }

    /// <summary>Raised when a persisted field (Enabled/Side) changes.</summary>
    public event Action? Changed;

    partial void OnEnabledChanged(bool value) => Changed?.Invoke();
    partial void OnSideChanged(string value) => Changed?.Invoke();
}
