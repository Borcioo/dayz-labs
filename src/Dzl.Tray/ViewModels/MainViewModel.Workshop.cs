using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Workshop;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // === Steam Workshop (SP5) =============================================

    public ObservableCollection<WorkshopItem> WorkshopResults { get; } = new();

    /// <summary>Items subscribed in the Steam client (its content folder) — what the Launcher loads.</summary>
    public ObservableCollection<SubscribedItem> WorkshopSubscribed { get; } = new();

    /// <summary>Items downloaded manually via steamcmd into &lt;ProjectsRoot&gt;\workshop.</summary>
    public ObservableCollection<SubscribedItem> WorkshopDownloaded { get; } = new();

    /// <summary>Sort options + time frames (from the Workshop browse page) for the toolbar combos.</summary>
    public IReadOnlyList<WorkshopSort> WorkshopSorts => WorkshopWeb.Sorts;
    public IReadOnlyList<WorkshopTimeFrame> WorkshopTimeFrames => WorkshopWeb.TimeFrames;

    /// <summary>Filter tags (Type + DayZ Mod-Type categories) — toggled, AND-ed into the query.</summary>
    public ObservableCollection<WorkshopCategoryVm> WorkshopFilters { get; } = new();

    [ObservableProperty] private string _workshopQuery = "";
    [ObservableProperty] private string _workshopStatus = "";
    [ObservableProperty] private WorkshopSort? _selectedSort;
    [ObservableProperty] private WorkshopTimeFrame? _selectedTimeFrame;
    [ObservableProperty] private bool _showTimeFrame = true;
    [ObservableProperty] private WorkshopItem? _selectedWorkshopItem;
    [ObservableProperty] private WorkshopItem? _workshopDetail;
    [ObservableProperty] private bool _detailSubscribed;   // is the item shown in the details pane already subscribed?
    private int _workshopPage = 1;
    private bool _wsReady;
    // Browse generation: every new browse bumps it; stale in-flight results are dropped, so two
    // quickly-toggled filter chips can't interleave their result pages.
    private int _browseGen;

    /// <summary>Build the filter tag list + sort/time-frame defaults (once). Call before showing the window.</summary>
    public void InitWorkshop()
    {
        if (_wsReady) return;
        foreach (var t in WorkshopWeb.Types.Concat(WorkshopWeb.ModTypes))
        {
            var c = new WorkshopCategoryVm(t);
            c.Toggled += () => { if (_wsReady) _ = WorkshopBrowseAsync(); };
            WorkshopFilters.Add(c);
        }
        SelectedSort = WorkshopSorts[0];                 // Most Popular (partial handlers no-op until _wsReady)
        SelectedTimeFrame = WorkshopTimeFrames[1];       // One Week
        _wsReady = true;
    }

    private IEnumerable<string> SelectedTags() => WorkshopFilters.Where(f => f.Selected).Select(f => f.Name);

    partial void OnSelectedSortChanged(WorkshopSort? value)
    {
        ShowTimeFrame = value?.BrowseSort == "trend";
        if (_wsReady) _ = WorkshopBrowseAsync();
    }

    partial void OnSelectedTimeFrameChanged(WorkshopTimeFrame? value) { if (_wsReady && ShowTimeFrame) _ = WorkshopBrowseAsync(); }

    partial void OnSelectedWorkshopItemChanged(WorkshopItem? value) => _ = LoadDetailAsync(value);

    partial void OnWorkshopDetailChanged(WorkshopItem? value) => RecomputeDetailSubscribed();

    /// <summary>Refresh <see cref="DetailSubscribed"/> from the current detail item + subscribed list.</summary>
    private void RecomputeDetailSubscribed()
        => DetailSubscribed = WorkshopDetail is { } d && WorkshopSubscribed.Any(s => s.Id == d.Id);

    /// <summary>Reload the subscribed-items list (Steam client content folder).</summary>
    public void RefreshSubscribed()
    {
        var svc = new WorkshopService(_configPath);
        WorkshopSubscribed.Clear();
        foreach (var s in svc.Subscribed()) WorkshopSubscribed.Add(s);
        WorkshopDownloaded.Clear();
        foreach (var s in svc.DownloadedItems()) WorkshopDownloaded.Add(s);
        RecomputeDetailSubscribed();
    }

    /// <summary>Browse with the current sort + time frame + selected category tags + search (page 1).</summary>
    public async Task WorkshopBrowseAsync()
    {
        var gen = ++_browseGen;
        _workshopPage = 1;
        var sort = SelectedSort?.BrowseSort ?? "trend";
        var days = ShowTimeFrame ? (SelectedTimeFrame?.Days ?? 7) : 0;
        var tags = SelectedTags().ToList();
        WorkshopStatus = "loading…";
        var (ok, error, items) = await new WorkshopService(_configPath).BrowseAsync(sort, days, WorkshopQuery, 30, 1, tags);
        if (gen != _browseGen) return;   // a newer browse owns the list now
        WorkshopResults.Clear();
        foreach (var it in items) WorkshopResults.Add(it);
        SelectedWorkshopItem = WorkshopResults.FirstOrDefault();
        var f = tags.Count > 0 ? " · " + string.Join("+", tags) : "";
        WorkshopStatus = ok ? $"{items.Count} result(s){f}" : $"✗ {error}";
    }

    /// <summary>Append the next page of the current browse.</summary>
    public async Task WorkshopLoadMoreAsync()
    {
        var gen = _browseGen;
        _workshopPage++;
        var sort = SelectedSort?.BrowseSort ?? "trend";
        var days = ShowTimeFrame ? (SelectedTimeFrame?.Days ?? 7) : 0;
        WorkshopStatus = "loading more…";
        var (ok, error, items) = await new WorkshopService(_configPath).BrowseAsync(sort, days, WorkshopQuery, 30, _workshopPage, SelectedTags().ToList());
        if (gen != _browseGen) return;   // the query changed mid-flight — don't append page N of an old browse
        foreach (var it in items) WorkshopResults.Add(it);
        WorkshopStatus = ok ? $"{WorkshopResults.Count} total (page {_workshopPage})" : $"✗ {error}";
    }

    /// <summary>True when a Steam session is stored (signed in). Backed by a field rather than a live
    /// getter — constructing a <see cref="WorkshopService"/> + reading the session off disk on every
    /// binding evaluation was the disease. Refreshed by <see cref="NotifyWorkshopGate"/> /
    /// <see cref="RefreshSteamAccount"/> (the existing call sites after any sign-in/out).</summary>
    [ObservableProperty] private bool _steamSignedIn;

    /// <summary>Recompute <see cref="SteamSignedIn"/> from the stored Steam session (disk read).</summary>
    private void RefreshSteamSignedIn() => SteamSignedIn = new WorkshopService(_configPath).SignedIn;

    /// <summary>True when steamcmd is configured + present (drives the Workshop Download gating).</summary>
    public bool SteamCmdConfigured => !string.IsNullOrWhiteSpace(_cfg.SteamCmdPath) && File.Exists(_cfg.SteamCmdPath);

    /// <summary>Re-evaluate the Workshop page's gating flags (sign-in / steamcmd) after a change.</summary>
    public void NotifyWorkshopGate()
    {
        RefreshSteamSignedIn();
        OnPropertyChanged(nameof(SteamCmdConfigured));
    }

    /// <summary>Steam account label for the Settings → Accounts row (reflects the stored sign-in).</summary>
    [ObservableProperty] private string _steamAccount = "not signed in";

    /// <summary>Refresh the Steam account label from the stored session + the account name saved on sign-in.
    /// Also recomputes <see cref="SteamSignedIn"/> from disk (so its bindings stay live without a getter).</summary>
    public void RefreshSteamAccount()
    {
        RefreshSteamSignedIn();
        SteamAccount = SteamSignedIn
            ? (string.IsNullOrWhiteSpace(Cfg.SteamLogin) ? "signed in" : $"logged in as {Cfg.SteamLogin}")
            : "not signed in";
    }

    public Task<Dzl.Core.Workshop.SteamLoginResult> SteamLoginQrAsync(Action<string> onUrl, System.Threading.CancellationToken ct)
        => new WorkshopService(_configPath).LoginViaQrAsync(onUrl, ct);

    public Task<Dzl.Core.Workshop.SteamLoginResult> SteamLoginCredentialsAsync(string user, string pass, SteamKit2.Authentication.IAuthenticator auth, System.Threading.CancellationToken ct)
        => new WorkshopService(_configPath).LoginViaCredentialsAsync(user, pass, auth, ct);

    public void SteamSignOut() => new WorkshopService(_configPath).SignOut();

    /// <summary>Subscribe in-app via the Steam web token if set; returns false (so the caller opens the Steam
    /// page) when no token is configured.</summary>
    public async Task<bool> SubscribeWorkshopAsync(string id)
    {
        var svc = new WorkshopService(_configPath);
        if (!svc.HasAccessToken) return false;
        WorkshopStatus = "subscribing…";
        var (ok, msg) = await svc.SubscribeAsync(id, true);
        WorkshopStatus = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
        {
            RefreshSubscribed();   // reflect the Steam client folder (may lag — download is async)
            // Optimistic: show it as subscribed immediately even before Steam finishes downloading it.
            if (!WorkshopSubscribed.Any(s => s.Id == id))
            {
                var title = WorkshopResults.FirstOrDefault(r => r.Id == id)?.Title
                            ?? (WorkshopDetail?.Id == id ? WorkshopDetail!.Title : null) ?? id;
                WorkshopSubscribed.Insert(0, new SubscribedItem(id, $"{title}  (downloading…)", ""));
            }
            RecomputeDetailSubscribed();
        }
        return true;
    }

    /// <summary>Unsubscribe a mod (needs a Steam session); removes it from the Subscribed list on success.</summary>
    public async Task UnsubscribeWorkshopAsync(string id)
    {
        var svc = new WorkshopService(_configPath);
        if (!svc.HasAccessToken) { WorkshopStatus = "✗ sign in to Steam to unsubscribe (Settings → Steam)"; return; }
        WorkshopStatus = "unsubscribing…";
        var (ok, msg) = await svc.SubscribeAsync(id, subscribe: false);
        WorkshopStatus = (ok ? "✓ " : "✗ ") + msg;
        if (ok)
        {
            var it = WorkshopSubscribed.FirstOrDefault(s => s.Id == id);
            if (it is not null) WorkshopSubscribed.Remove(it);
            RecomputeDetailSubscribed();
        }
    }

    /// <summary>Show an item's details in the right pane by id (e.g. from the Subscribed/Downloaded lists).
    /// Selects it in the results when present (which loads details), otherwise fetches details directly.</summary>
    public async Task ShowDetailAsync(string id)
    {
        var inResults = WorkshopResults.FirstOrDefault(r => r.Id == id);
        if (inResults is not null) { SelectedWorkshopItem = inResults; return; }
        WorkshopDetail = new WorkshopItem(id, id);   // placeholder until the fetch lands
        var full = await new WorkshopService(_configPath).DetailsAsync(id);
        if (full is not null) WorkshopDetail = full;
    }

    // Show the list item immediately in the details pane, then enrich (subs/description/tags) keylessly.
    private async Task LoadDetailAsync(WorkshopItem? item)
    {
        WorkshopDetail = item;
        if (item is null) return;
        var full = await new WorkshopService(_configPath).DetailsAsync(item.Id);
        if (full is not null && SelectedWorkshopItem?.Id == item.Id) WorkshopDetail = full;
    }

    /// <summary>Auto-install steamcmd into the config dir; returns (ok, exe path, message).</summary>
    public Task<(bool ok, string path, string error)> InstallSteamCmdAsync()
        => InstallSteamCmdCore(Path.Combine(ConfigDir, "steamcmd"));

    private static async Task<(bool ok, string path, string error)> InstallSteamCmdCore(string dest)
    {
        var (ok, exe, msg) = await SteamCmdInstaller.InstallAsync(dest);
        return (ok, exe, msg);
    }

    /// <summary>Download a Workshop item by id via steamcmd (opens a console). Runs off the UI thread;
    /// the status line hops back. Returns the status line.</summary>
    public async Task<string> WorkshopDownloadAsync(string id)
    {
        var cp = _configPath;
        WorkshopStatus = "downloading…";
        var r = await Task.Run(() => new WorkshopService(cp).Download(id));
        WorkshopStatus = (r.Ok ? "✓ " : "✗ ") + r.Message;
        return WorkshopStatus;
    }

    /// <summary>Where item <paramref name="id"/> actually lives on disk (Steam client folder or the steamcmd
    /// download under ProjectsRoot), or null if it isn't downloaded yet.</summary>
    public string? ResolveModFolder(string id) => new WorkshopService(_configPath).ResolveContentDir(id);

    /// <summary>Delete a steamcmd-downloaded item (junction + cached files) off the UI thread, then refresh
    /// the lists on the UI thread. Returns the status line.</summary>
    public async Task<string> DeleteDownloadedAsync(string id)
    {
        var cp = _configPath;
        var (ok, msg) = await Task.Run(() => new WorkshopService(cp).DeleteDownloaded(id));
        WorkshopStatus = (ok ? "✓ " : "✗ ") + msg;
        if (ok) RefreshSubscribed();
        return WorkshopStatus;
    }
}
