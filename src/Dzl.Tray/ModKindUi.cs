using System.Windows.Media;
using Dzl.Core.Mods;

namespace Dzl.Tray;

/// <summary>Display text + pill colors for a <see cref="ModKind"/>, shared by every mod-row view model so the
/// source/type badge looks identical everywhere it appears.</summary>
internal static class ModKindUi
{
    public static string Label(ModKind k) => k switch
    {
        ModKind.Source => "Source",
        ModKind.Build => "Build",
        ModKind.Workshop => "Workshop",
        ModKind.Downloaded => "steamcmd",
        _ => "External",
    };

    public static Brush Bg(ModKind k) => Brushes[k].bg;
    public static Brush Fg(ModKind k) => Brushes[k].fg;

    private static readonly Dictionary<ModKind, (Brush bg, Brush fg)> Brushes = new()
    {
        [ModKind.Source]     = Pair("#1E3A5A", "#7FB6FF"),   // blue   — your uncompiled source
        [ModKind.Build]      = Pair("#1F4D33", "#7CE3A1"),   // green  — your compiled PBO
        [ModKind.Workshop]   = Pair("#1B3B4D", "#66C0F4"),   // steam  — subscribed in the client
        [ModKind.Downloaded] = Pair("#4D3A1A", "#F0C04A"),   // amber  — pulled via steamcmd
        [ModKind.External]   = Pair("#3A3A3A", "#BBBBBB"),   // gray   — outside the structure
    };

    private static (Brush, Brush) Pair(string bg, string fg) => (Freeze(bg), Freeze(fg));

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
