using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Dzl.Tray;

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> against the GitHub Releases feed. All calls are
/// no-ops unless running as an installed Velopack app (so dev `dotnet run` is safe). The
/// download/apply path restarts the app onto the new version.
/// </summary>
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/Borcioo/dayz-labs";

    // UpdateManager's ctor reads VelopackLocator.Current, which THROWS until VelopackApp.Build().Run()
    // has run (i.e. in a dev/test host that never bootstrapped Velopack). So we build it lazily and
    // only once a locator is set; otherwise every member stays a safe no-op (CanUpdate == false).
    private readonly UpdateManager? _mgr =
        VelopackLocator.IsCurrentSet
            ? new UpdateManager(new GithubSource(RepoUrl, null, false))
            : null;

    /// <summary>True only when this is an installed Velopack app (not a dev build).</summary>
    public bool CanUpdate => _mgr?.IsInstalled ?? false;

    /// <summary>Returns the available update, or null if up to date / not installed.</summary>
    public async Task<UpdateInfo?> CheckAsync()
        => _mgr is { IsInstalled: true } ? await _mgr.CheckForUpdatesAsync() : null;

    /// <summary>Downloads the update, then waits for this process to exit and applies it,
    /// restarting the app. Call last — the app should shut down right after.</summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info)
    {
        if (_mgr is null) return;
        await _mgr.DownloadUpdatesAsync(info);
        _mgr.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: false, restart: true);
    }
}
