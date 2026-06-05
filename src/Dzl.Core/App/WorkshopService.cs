using Dzl.Core.Config;
using Dzl.Core.Mods;
using Dzl.Core.Workshop;
using SteamKit2.Authentication;

namespace Dzl.Core.App;

public sealed record WorkshopOp(bool Ok, string Message);

/// <summary>A Workshop item already subscribed/downloaded in the Steam client's content folder.</summary>
public sealed record SubscribedItem(string Id, string Name, string Dir);

/// <summary>
/// SP5 Steam Workshop: search (Steam Web API — needs a key) and download/update (steamcmd — owned/DayZ
/// items need a Steam login). One facade per frontend; HTTP + process work live in <c>Dzl.Core.Workshop</c>.
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
        if (!string.IsNullOrEmpty(_accessCache) && DateTime.UtcNow - _accessAt < TimeSpan.FromHours(12)) return (_accessCache, "");
        using var auth = new SteamAuth();
        var (token, error) = await auth.RenewAccessTokenAsync(refresh);
        if (!string.IsNullOrEmpty(token)) { _accessCache = token; _accessAt = DateTime.UtcNow; }
        return (token, string.IsNullOrEmpty(token) ? $"couldn't refresh Steam session ({error}) — sign in again" : "");
    }

    /// <summary>In-app subscribe (true) / unsubscribe (false) — renews the access token from the stored session.</summary>
    public async Task<(bool ok, string message)> SubscribeAsync(string id, bool subscribe = true)
    {
        var (token, error) = await AccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return (false, error.Length > 0 ? error : "not signed in to Steam");
        return await WorkshopWeb.SubscribeAsync(token, id, subscribe);
    }

    /// <summary>QR sign-in; on success stores the refresh token (encrypted) for future Subscribe calls.</summary>
    public async Task<SteamLoginResult> LoginViaQrAsync(Action<string> onChallengeUrl, CancellationToken ct)
    {
        using var auth = new SteamAuth();
        var r = await auth.LoginViaQrAsync(onChallengeUrl, ct);
        if (r.Ok && !string.IsNullOrWhiteSpace(r.RefreshToken))
        {
            SteamTokenStore.Save(_configPath, r.RefreshToken);
            _accessCache = r.AccessToken; _accessAt = DateTime.UtcNow;
        }
        return r;
    }

    /// <summary>Username/password sign-in (+ Steam Guard via <paramref name="authenticator"/>).</summary>
    public async Task<SteamLoginResult> LoginViaCredentialsAsync(string user, string pass, IAuthenticator authenticator, CancellationToken ct)
    {
        using var auth = new SteamAuth();
        var r = await auth.LoginViaCredentialsAsync(user, pass, authenticator, ct);
        if (r.Ok && !string.IsNullOrWhiteSpace(r.RefreshToken))
        {
            SteamTokenStore.Save(_configPath, r.RefreshToken);
            _accessCache = r.AccessToken; _accessAt = DateTime.UtcNow;
        }
        return r;
    }

    /// <summary>Forget the stored Steam session.</summary>
    public void SignOut() { SteamTokenStore.Clear(_configPath); _accessCache = null; }

    /// <summary>Download (or re-download to update) a Workshop item via steamcmd, spawning a console for login.</summary>
    public WorkshopOp Download(string id)
    {
        var cfg = Cfg;
        if (string.IsNullOrWhiteSpace(cfg.SteamCmdPath) || !File.Exists(cfg.SteamCmdPath))
            return new(false, "steamcmd not found — set its path in Settings");
        if (string.IsNullOrWhiteSpace(id))
            return new(false, "workshop id required");
        return WorkshopCmd.Download(cfg.SteamCmdPath, cfg.SteamLogin, id)
            ? new(true, $"launched steamcmd for {id} — complete any login / Steam Guard in the console window")
            : new(false, "could not launch steamcmd");
    }

    /// <summary>Workshop item ids already downloaded into steamcmd's content folder.</summary>
    public List<string> Downloaded()
    {
        var cfg = Cfg;
        if (string.IsNullOrWhiteSpace(cfg.SteamCmdPath)) return new();
        var dir = Path.Combine(Path.GetDirectoryName(cfg.SteamCmdPath) ?? ".", "steamapps", "workshop", "content", WorkshopCmd.AppId);
        if (!Directory.Exists(dir)) return new();
        try { return Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)!).Where(s => s.Length > 0).ToList(); }
        catch { return new(); }
    }

    /// <summary>Local folder a downloaded item lands in (or null when steamcmd isn't configured).</summary>
    public string? ContentDir(string id)
    {
        var cfg = Cfg;
        return string.IsNullOrWhiteSpace(cfg.SteamCmdPath) ? null : WorkshopCmd.ContentDir(cfg.SteamCmdPath, id);
    }

    /// <summary>Items subscribed in the Steam <i>client</i> (its workshop content folder, resolved from the
    /// Steam install) — these are what the DayZ Launcher loads. Friendly names via meta.cpp/mod.cpp.</summary>
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

    /// <summary>Steam client URL that opens a Workshop item's page (where the user clicks Subscribe).</summary>
    public static string SteamPageUrl(string id) => $"steam://url/CommunityFilePage/{id}";
}
