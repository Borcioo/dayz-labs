using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Dzl.Tray;

namespace Dzl.Tray.Views;

/// <summary>About page: app version, links (repo / site / releases), license, and a manual
/// "Check for updates" that reuses <see cref="UpdateService"/>.</summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = "Version " + AppVersion();
    }

    /// <summary>The release version (from AssemblyInformationalVersion, set by `-p:Version` at pack
    /// time), without the +commit suffix. "(dev build)" for an un-versioned local run.</summary>
    private static string AppVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "(dev build)";
        var plus = info.IndexOf('+');
        return plus > 0 ? info[..plus] : info;
    }

    private void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url }) Open(url);
    }

    private void OnOpenHyperlink(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { Tag: string url }) Open(url);
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        var svc = new UpdateService();
        if (!svc.CanUpdate) { UpdateStatus.Text = "Updates are available only in the installed app."; return; }

        CheckUpdatesBtn.IsEnabled = false;
        UpdateStatus.Text = "Checking…";
        try
        {
            var info = await svc.CheckAsync();
            if (info is null) { UpdateStatus.Text = "You're on the latest version."; return; }

            var version = info.TargetFullRelease.Version;
            UpdateStatus.Text = $"Version {version} is available.";
            var ok = MessageBox.Show(
                $"Version {version} is available. Update now? The app will restart.",
                "DayZ Labs — update available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
            if (ok) { await svc.DownloadAndApplyAsync(info); Application.Current.Shutdown(); }
        }
        catch (Exception ex) { UpdateStatus.Text = "Update check failed: " + ex.Message; }
        finally { CheckUpdatesBtn.IsEnabled = true; }
    }
}
