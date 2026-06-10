using System;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray;

namespace Dzl.Tray.Controls;

/// <summary>
/// Drop-in "capture the app" affordance for any window or dialog: a camera button plus a
/// clickable "✓ saved …" status. The capture grabs the WHOLE app (main window + every open
/// child window) via <see cref="AppScreenshot"/>, so it behaves identically no matter which
/// window hosts the button. Clicking the status reveals the PNG in Explorer (or the
/// screenshots folder if the file is gone).
/// </summary>
public partial class ScreenshotButton : UserControl
{
    private string? _lastPath;

    public ScreenshotButton()
    {
        InitializeComponent();
    }

    private async void OnScreenshot(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "capturing…";
        await System.Threading.Tasks.Task.Delay(150);   // let the button's pressed state clear before the grab
        try
        {
            var path = AppScreenshot.Capture(App.ConfigPath());
            _lastPath = path;
            StatusText.Text = $"✓ saved {System.IO.Path.GetFileName(path)}";
            StatusText.ToolTip = $"{path}\nClick to show in Explorer";
        }
        catch (Exception ex) { _lastPath = null; StatusText.Text = "✗ " + ex.Message; }
    }

    // Click on the "✓ saved …" status → reveal the file in Explorer (or just the screenshots
    // folder if the file is gone). No-op when nothing was captured yet.
    private void OnRevealScreenshot(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var path = _lastPath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (System.IO.File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (System.IO.Path.GetDirectoryName(path) is { } dir && System.IO.Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch { /* opening Explorer is best-effort; never crash the host window */ }
    }
}
