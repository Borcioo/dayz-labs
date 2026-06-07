using System.Drawing;          // Bitmap / Graphics.CopyFromScreen (System.Drawing.Common, already used by the tray icon)
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Dzl.Tray;

/// <summary>
/// Captures the app as it looks on screen — the main window plus every open child window (Workshop, modals) —
/// by grabbing the screen region that bounds all of them. Uses the same System.Drawing path the tray icon
/// already relies on, so no new dependency. Saves a PNG under &lt;configDir&gt;\screenshots and returns its path.
/// </summary>
internal static class AppScreenshot
{
    public static string Capture(string configPath)
    {
        // Only our own real windows (MainWindow / Workshop / modals). Excludes the hidden H.NotifyIcon tray-host
        // window — it lives in another assembly and would otherwise blow the union out to empty desktop.
        var ours = typeof(MainWindow).Assembly;
        var windows = Application.Current.Windows.OfType<Window>()
            .Where(w => w.GetType().Assembly == ours && w.IsVisible
                        && w.WindowState != WindowState.Minimized && w.ActualWidth > 0 && w.ActualHeight > 0)
            .ToList();
        if (windows.Count == 0) throw new InvalidOperationException("no visible windows to capture");

        // DPI scale (use the primary window's; good enough for same-monitor setups).
        var dpi = VisualTreeHelper.GetDpi(windows[0]);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

        // Union of window bounds (DIP) → physical pixels.
        double left = windows.Min(w => w.Left), top = windows.Min(w => w.Top);
        double right = windows.Max(w => w.Left + w.ActualWidth), bottom = windows.Max(w => w.Top + w.ActualHeight);
        int px = (int)Math.Floor(left * sx), py = (int)Math.Floor(top * sy);
        int pw = Math.Max(1, (int)Math.Ceiling((right - left) * sx));
        int ph = Math.Max(1, (int)Math.Ceiling((bottom - top) * sy));

        // Clamp to the virtual screen so a maximized overhang or odd position can't pull in off-screen pixels.
        int vL = (int)Math.Floor(SystemParameters.VirtualScreenLeft * sx);
        int vT = (int)Math.Floor(SystemParameters.VirtualScreenTop * sy);
        int vR = vL + (int)Math.Ceiling(SystemParameters.VirtualScreenWidth * sx);
        int vB = vT + (int)Math.Ceiling(SystemParameters.VirtualScreenHeight * sy);
        if (px < vL) { pw -= vL - px; px = vL; }
        if (py < vT) { ph -= vT - py; py = vT; }
        pw = Math.Max(1, Math.Min(pw, vR - px));
        ph = Math.Max(1, Math.Min(ph, vB - py));

        using var bmp = new Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(px, py, 0, 0, new System.Drawing.Size(pw, ph), CopyPixelOperation.SourceCopy);

        var dir = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"dzl-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
