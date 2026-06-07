using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Dzl.Core.App;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>
/// Standalone Steam Workshop browser: search the Web API, then per result Subscribe (opens the item in the
/// Steam client) or Download (steamcmd). Lists items already subscribed in the Steam client with open/update
/// actions. Shares <see cref="MainViewModel"/> so results/subscribed/status bind directly.
/// </summary>
public partial class WorkshopWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    public WorkshopWindow(MainViewModel vm)
    {
        _vm = vm;
        _vm.InitWorkshop();          // build filter list + sort/time-frame defaults before InitializeComponent binds them
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            _vm.RefreshSubscribed();
            await _vm.WorkshopBrowseAsync();   // open on the current sort (Most Popular / One Week)
        };
    }

    // ⚙ — open the Workshop settings modal (Steam sign-in + steamcmd); refresh gating after.
    private void OnWorkshopSettings(object sender, RoutedEventArgs e)
    {
        new ModuleSettingsWindow(_vm, "workshop") { Owner = this }.ShowDialog();
        _vm.NotifyWorkshopGate();
        _vm.RefreshSubscribed();
    }

    // Sign-in banner button — sign in directly, then re-evaluate the gate.
    private void OnSignInBanner(object sender, RoutedEventArgs e)
    {
        new SteamLoginWindow(_vm) { Owner = this }.ShowDialog();
        _vm.NotifyWorkshopGate();
        _vm.RefreshSubscribed();
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await _vm.WorkshopBrowseAsync();

    // Infinite scroll: auto-load the next page when the results list is scrolled near the bottom. With a
    // virtualizing ListBox the ScrollViewer offsets are in item units, so "within 3 of the end" works.
    private bool _loadingMore;
    private async void OnResultsScroll(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (_loadingMore || e.VerticalChange <= 0 || e.ExtentHeight <= e.ViewportHeight) return;
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 3) return;   // not near the bottom yet
        _loadingMore = true;
        try { await _vm.WorkshopLoadMoreAsync(); }
        finally { _loadingMore = false; }
    }

    private void OnRefreshSubscribed(object sender, RoutedEventArgs e) => _vm.RefreshSubscribed();

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) _vm.WorkshopDownload(id);
    }

    // Subscribe in-app via the Steam web token when set; otherwise open the item page in the Steam client.
    private async void OnSubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        if (await _vm.SubscribeWorkshopAsync(id)) return;   // handled in-app
        try { Process.Start(new ProcessStartInfo(WorkshopService.SteamPageUrl(id)) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    // Always open the item's Steam page (manage/unsubscribe there) — distinct from Subscribe (which acts in-app).
    private void OnOpenInSteam(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        try { Process.Start(new ProcessStartInfo(WorkshopService.SteamPageUrl(id)) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    private async void OnUnsubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await _vm.UnsubscribeWorkshopAsync(id);
    }

    // Resolve the item's real folder by id (Steam client OR the steamcmd download under ProjectsRoot) — the
    // SubscribedItem.Dir reflects only the Steam client folder and is empty for optimistic / steamcmd-only rows.
    private void OnOpenSubscribedFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        var dir = _vm.ResolveModFolder(id);
        if (string.IsNullOrWhiteSpace(dir) || !ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show("Not downloaded yet — subscribe (the Steam client downloads in the background) or use Download (steamcmd).",
                "Open folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // Delete a steamcmd-downloaded item (destructive → confirm first).
    private void OnDeleteDownloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        var name = _vm.WorkshopDownloaded.FirstOrDefault(d => d.Id == id)?.Name ?? id;
        var r = System.Windows.MessageBox.Show(
            $"Delete the downloaded files for \"{name}\" ({id})?\n\nThis removes them from your workshop folder. You can re-download later.",
            "Delete download", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes) _vm.DeleteDownloaded(id);
    }

    private void OnAddById(object sender, RoutedEventArgs e)
    {
        var input = PromptDialog.Show(this, "Add Workshop item", "Workshop id or URL:");
        if (string.IsNullOrWhiteSpace(input)) return;
        var m = System.Text.RegularExpressions.Regex.Match(input, @"id=(\d+)");
        var id = m.Success ? m.Groups[1].Value : new string(input.Where(char.IsDigit).ToArray());
        if (id.Length == 0)
        {
            System.Windows.MessageBox.Show("Couldn't find a Workshop id in that input.", "Add by ID",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        _vm.WorkshopDownload(id);
    }
}
