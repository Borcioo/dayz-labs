using Dzl.Core.Config;
using Dzl.Core.Mods;
using Dzl.Core.Workshop;
using SteamKit2.Authentication;

namespace Dzl.Core.App;

/// <summary>A Workshop item subscribed/downloaded in the Steam client's content folder.</summary>
public sealed record SubscribedItem(string Id, string Name, string Dir);

/// <summary>
/// Steam Workshop facade: keyless search/browse plus download/update via steamcmd (owned/DayZ
/// items need a Steam login). HTTP + process work live in <c>Dzl.Core.Workshop</c>.
/// </summary>
public sealed class WorkshopService
{
    private readonly string _configPath;
    public WorkshopService(string configPath) { _configPath = configPath; }

    private DzlConfig Cfg => Profiles.ResolveActive(_configPath).cfg;

    /// <summary>Text search (keyless) — used by CLI/MCP. Returns (ok, error, items); never throws.</summary>
    public Task<(bool ok, string error, List<WorkshopItem> items)> SearchAsync(string query, int count = 30)
        => WorkshopWeb.BrowseAsync("trend", 0, query, count, 1, null);

    /// <summary>Full keyless browse: sort + time-frame (days) + DayZ category tags + text search + page.</summary>
    public Task<(bool ok, string error, List<WorkshopItem> items)> BrowseAsync(
        string browseSort, int days, string query, int count, int page, IEnumerable<string>? tags = null)
        => WorkshopWeb.BrowseAsync(browseSort, days, query, count, page, tags);

    /// <summary>Full details for one item (subscribers/description/tags) via the keyless details endpoint.</summary>
    public Task<WorkshopItem?> DetailsAsync(string id) => WorkshopWeb.DetailsAsync(id);

    // Cached access token (short-lived) minted from the stored refresh token; renewed on demand.
    private static string? _accessCache;
    private static DateTime _accessAt;

    /// <summary>True when in-app Subscribe is possible — a signed-in Steam session (stored refresh token) or
    /// an explicitly pasted access token.</summary>
    public bool HasAccessToken => SignedIn || !string.IsNullOrWhiteSpace(Cfg.SteamAccessToken);

    /// <summary>True when a Steam session (refresh token) is stored.</summary>
    public bool SignedIn => SteamTokenStore.Exists(_configPath);

    /// <summary>A usable access token: the explicit pasted one, else one renewed (and cached ~12h) from the
    /// stored refresh token. Returns (token, error) — token null with a reason when unavailable.</summary>
    private async Task<(string? token, string error)> AccessTokenAsync()
    {
        var pasted = Cfg.SteamAccessToken;
        if (!string.IsNullOrWhiteSpace(pasted)) return (pasted.Trim(), "");
        var refresh = SteamTokenStore.Load(_configPath);
        if (refresh is null) return (null, "not signed in to Steam");
        // 1) in-memory cache (this process)  2) disk-cached access token from a prior run — reuse while it's still
        // valid (~24h) since renewing from the refresh token is unreliable. Only mint a new one once it's expired.
        if (!string.IsNullOrEmpty(_accessCache) && SteamAuth.TokenStillValid(_accessCache)) return (_accessCache, "");
        var stored = SteamTokenStore.LoadAccess(_configPath);
        if (!string.IsNullOrEmpty(stored) && SteamAuth.TokenStillValid(stored))
        {
            _accessCache = stored; _accessAt = DateTime.UtcNow;
            return (stored, "");
        }
        using var auth = new SteamAuth();
        var (token, error) = await auth.RenewAccessTokenAsync(refresh);
        if (!string.IsNullOrEmpty(token)) { _accessCache = token; _accessAt = DateTime.UtcNow; SteamTokenStore.SaveAccess(_configPath, token); }
        return (token, string.IsNullOrEmpty(token) ? $"couldn't refresh Steam session ({error}) — sign in again" : "");
    }

    /// <summary>In-app subscribe (true) / unsubscribe (false) — renews the access token from the stored session.</summary>
    public async Task<OpResult> SubscribeAsync(string id, bool subscribe = true)
    {
        var (token, error) = await AccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return new(false, error.Length > 0 ? error : "not signed in to Steam");
        var (ok, message) = await WorkshopWeb.SubscribeAsync(token, id, subscribe);
        return new(ok, message);
    }

    /// <summary>QR sign-in; on success stores the refresh token (encrypted) for future Subscribe calls.</summary>
    public async Task<SteamLoginResult> LoginViaQrAsync(Action<string> onChallengeUrl, CancellationToken ct)
    {
        using var auth = new SteamAuth();
        var r = await auth.LoginViaQrAsync(onChallengeUrl, ct);
        if (r.Ok && !string.IsNullOrWhiteSpace(r.RefreshToken)) OnSignedIn(r);
        return r;
    }

    /// <summary>Username/password sign-in (+ Steam Guard via <paramref name="authenticator"/>).</summary>
    public async Task<SteamLoginResult> LoginViaCredentialsAsync(string user, string pass, IAuthenticator authenticator, CancellationToken ct)
    {
        using var auth = new SteamAuth();
        var r = await auth.LoginViaCredentialsAsync(user, pass, authenticator, ct);
        if (r.Ok && !string.IsNullOrWhiteSpace(r.RefreshToken)) OnSignedIn(r);
        return r;
    }

    /// <summary>On a successful sign-in: cache the access token, persist the refresh token, and remember the
    /// account name so steamcmd downloads use it (DayZ Workshop items can't be fetched anonymously).</summary>
    private void OnSignedIn(SteamLoginResult r)
    {
        SteamTokenStore.Save(_configPath, r.RefreshToken);
        _accessCache = r.AccessToken; _accessAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(r.AccessToken)) SteamTokenStore.SaveAccess(_configPath, r.AccessToken);
        if (string.IsNullOrWhiteSpace(r.AccountName)) return;
        var (cfg, _, active) = Profiles.ResolveActive(_configPath);
        if (!string.Equals(cfg.SteamLogin, r.AccountName, StringComparison.OrdinalIgnoreCase))
            Profiles.Save(cfg with { SteamLogin = r.AccountName }, string.IsNullOrEmpty(active) ? "default" : active, _configPath);
    }

    /// <summary>Forget the stored Steam session.</summary>
    public void SignOut() { SteamTokenStore.Clear(_configPath); _accessCache = null; }

    /// <summary>steamcmd's install root for Workshop downloads — <c>&lt;ProjectsRoot&gt;\workshop</c>. Items end up
    /// at the clean <c>&lt;ProjectsRoot&gt;\workshop\&lt;id&gt;</c> (a junction to the hidden <c>.steamcmd</c> cache),
    /// not the deep <c>steamapps\workshop\content\221100</c> path steamcmd would otherwise use.</summary>
    private string WorkshopInstallDir()
    {
        var cfg = Cfg;
        return string.IsNullOrWhiteSpace(cfg.WorkshopDir)
            ? Dzl.Core.Projects.ProjectPaths.WorkshopDir(Dzl.Core.Projects.ProjectPaths.Root(cfg))
            : cfg.WorkshopDir;
    }

    /// <summary>The resolved Workshop download folder (for display in Settings).</summary>
    public string ResolvedWorkshopDir() => WorkshopInstallDir();

    /// <summary>Delete a downloaded item — removes the clean junction and the cached steamcmd content. Never throws.</summary>
    public OpResult DeleteDownloaded(string id)
    {
        try
        {
            var root = WorkshopInstallDir();
            var dest = WorkshopCmd.ContentDir(root, id);   // the clean <root>\<id> junction
            var raw = WorkshopCmd.RawDir(root, id);         // the cached real files in .steamcmd
            // On net6+ deleting a junction removes the link only (does not recurse into the target).
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            if (Directory.Exists(raw)) Directory.Delete(raw, true);
            return new(true, $"deleted {id}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>Download (or re-download to update) a Workshop item via steamcmd, spawning a console for login.</summary>
    public OpResult Download(string id)
    {
        var cfg = Cfg;
        if (string.IsNullOrWhiteSpace(cfg.SteamCmdPath) || !File.Exists(cfg.SteamCmdPath))
            return new(false, "steamcmd not found — set or install it via the ⚙ here (Workshop settings)");
        if (string.IsNullOrWhiteSpace(id))
            return new(false, "workshop id required");
        // DayZ Workshop content is owner-gated: an anonymous steamcmd login fails with "Download item … failed
        // (Failure)". Require a Steam account name (auto-filled on sign-in, or set in Settings → Steam).
        if (string.IsNullOrWhiteSpace(cfg.SteamLogin))
            return new(false, "DayZ Workshop items can't be downloaded anonymously — sign in to Steam (the ⚙ here, or Settings → Accounts) so steamcmd uses your account");
        var dir = WorkshopInstallDir();
        return WorkshopCmd.Download(cfg.SteamCmdPath, cfg.SteamLogin, id, dir)
            ? new(true, $"launched steamcmd for {id} as {cfg.SteamLogin} → {WorkshopCmd.ContentDir(dir, id)} — complete any password / Steam Guard prompt in the console window")
            : new(false, "could not launch steamcmd");
    }

    /// <summary>Workshop item ids already downloaded into <c>&lt;ProjectsRoot&gt;\workshop</c> (clean <c>&lt;id&gt;</c>
    /// folders; the hidden <c>.steamcmd</c> cache is skipped).</summary>
    public List<string> Downloaded()
    {
        var cfg = Cfg;
        if (string.IsNullOrWhiteSpace(cfg.SteamCmdPath)) return new();
        var dir = WorkshopInstallDir();
        if (!Directory.Exists(dir)) return new();
        try
        {
            return Directory.GetDirectories(dir)
                .Select(d => Path.GetFileName(d)!)
                .Where(s => s.Length > 0 && !s.StartsWith('.'))
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Local folder a downloaded item lands in (or null when steamcmd isn't configured).</summary>
    public string? ContentDir(string id)
    {
        var cfg = Cfg;
        return string.IsNullOrWhiteSpace(cfg.SteamCmdPath) ? null : WorkshopCmd.ContentDir(WorkshopInstallDir(), id);
    }

    /// <summary>The folder where item <paramref name="id"/> actually lives on disk, checking both places it
    /// can land: the Steam <i>client</i>'s workshop content folder (in-app Subscribe) and the steamcmd download
    /// folder under ProjectsRoot. Null when it isn't downloaded in either.</summary>
    public string? ResolveContentDir(string id)
    {
        var steam = Dzl.Core.Env.EnvDetect.SteamPath();
        if (steam is not null)
        {
            var d = Path.Combine(steam, "steamapps", "workshop", "content", WorkshopCmd.AppId, id);
            if (Directory.Exists(d)) return d;
        }
        var cd = ContentDir(id);
        return cd is not null && Directory.Exists(cd) ? cd : null;
    }

    /// <summary>Items in the Steam <i>client</i>'s workshop content folder (resolved from the Steam install) —
    /// what the DayZ Launcher loads. steamcmd downloads land here too (see <see cref="WorkshopInstallDir"/>), so
    /// this single Steam-folder scan covers both. Friendly names via meta.cpp/mod.cpp.</summary>
    public List<SubscribedItem> Subscribed()
    {
        var steam = Dzl.Core.Env.EnvDetect.SteamPath();
        if (steam is null) return new();
        var dir = Path.Combine(steam, "steamapps", "workshop", "content", WorkshopCmd.AppId);
        if (!Directory.Exists(dir)) return new();
        try
        {
            return Directory.GetDirectories(dir)
                .Select(d => new SubscribedItem(Path.GetFileName(d)!, ModDiscovery.ResolveName(d), d))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Items downloaded via steamcmd into <c>&lt;ProjectsRoot&gt;\workshop</c> (clean <c>&lt;id&gt;</c>
    /// folders; the hidden <c>.steamcmd</c> cache is skipped). Friendly names via meta.cpp/mod.cpp.</summary>
    public List<SubscribedItem> DownloadedItems()
    {
        var root = WorkshopInstallDir();
        if (!Directory.Exists(root)) return new();
        try
        {
            return Directory.GetDirectories(root)
                .Where(d => !Path.GetFileName(d)!.StartsWith('.'))
                .Select(d => new SubscribedItem(Path.GetFileName(d)!, ModDiscovery.ResolveName(d), d))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new(); }
    }

    /// <summary>Steam client URL that opens a Workshop item's page (where the user clicks Subscribe).</summary>
    public static string SteamPageUrl(string id) => $"steam://url/CommunityFilePage/{id}";
}
