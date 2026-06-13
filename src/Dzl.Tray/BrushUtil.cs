using System.Windows.Media;

namespace Dzl.Tray;

/// <summary>Tiny shared brush helpers for the tray's pill/badge code. <see cref="Freeze"/> parses a hex
/// color into a frozen <see cref="SolidColorBrush"/> (frozen = cross-thread-safe + cheaper to render);
/// it previously appeared verbatim in a handful of UI helpers.</summary>
internal static class BrushUtil
{
    /// <summary>Parse a hex color (e.g. <c>"#7CE3A1"</c>) into a frozen <see cref="SolidColorBrush"/>.</summary>
    public static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
