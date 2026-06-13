using System.Windows.Media;
using Dzl.Core.Economy;

namespace Dzl.Tray;

/// <summary>Display text + pill colors for a CE file <see cref="CeOrigin"/>, mirroring <see cref="ModKindUi"/>
/// so the source/origin badge on the Economy page matches the mod-source pills used elsewhere.</summary>
internal static class OriginUi
{
    public static string Label(CeOrigin o) => o switch
    {
        CeOrigin.Vanilla => "Vanilla",
        CeOrigin.Mod     => "Mod",
        _                => "Custom",
    };

    public static Brush Bg(CeOrigin o) => Brushes[o].bg;
    public static Brush Fg(CeOrigin o) => Brushes[o].fg;

    private static readonly Dictionary<CeOrigin, (Brush bg, Brush fg)> Brushes = new()
    {
        [CeOrigin.Vanilla] = Pair("#3A3A3A", "#BBBBBB"),   // gray  — stock game data
        [CeOrigin.Mod]     = Pair("#1F4D33", "#7CE3A1"),   // green — a mod's CE file
        [CeOrigin.Custom]  = Pair("#1E3A5A", "#7FB6FF"),   // blue  — mission-custom CE file
    };

    private static (Brush, Brush) Pair(string bg, string fg) => (BrushUtil.Freeze(bg), BrushUtil.Freeze(fg));
}
