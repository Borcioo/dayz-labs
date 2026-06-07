using System.Windows.Media;
using Dzl.Core.Mods;

namespace Dzl.Tray.ViewModels;

/// <summary>A mod as shown in the Dashboard's active-mods lists: its display label (with any "(side)" suffix)
/// plus the source/type badge, so an enabled mod reads the same — Source / Build / Workshop / steamcmd —
/// everywhere it appears.</summary>
public sealed class ActiveModVm
{
    public string Label { get; }
    public ModKind Kind { get; }

    public ActiveModVm(string label, ModKind kind) { Label = label; Kind = kind; }

    public string KindLabel => Dzl.Tray.ModKindUi.Label(Kind);
    public Brush KindBg => Dzl.Tray.ModKindUi.Bg(Kind);
    public Brush KindFg => Dzl.Tray.ModKindUi.Fg(Kind);
}
