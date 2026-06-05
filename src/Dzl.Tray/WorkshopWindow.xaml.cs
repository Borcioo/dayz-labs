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

    private async void OnSearch(object sender, RoutedEventArgs e) => await _vm.WorkshopBrowseAsync();

    private async void OnLoadMore(object sender, RoutedEventArgs e) => await _vm.WorkshopLoadMoreAsync();

    private void OnRefreshSubscribed(object sender, RoutedEventArgs e) => _vm.RefreshSubscribed();

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) _vm.WorkshopDownload(id);
    }

    // Subscribe / manage in the Steam client (Steam blocks auto-subscribe from outside — opens the page).
    private void OnSubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        try { Process.Start(new ProcessStartInfo(WorkshopService.SteamPageUrl(id)) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    private void OnOpenSubscribedFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir } || !Directory.Exists(dir)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true }); }
        catch { /* best-effort */ }
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
