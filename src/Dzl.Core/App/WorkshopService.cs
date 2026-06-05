using Dzl.Core.Config;
using Dzl.Core.Mods;
using Dzl.Core.Workshop;

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

    /// <summary>Search the Workshop (needs a Steam Web API key). Returns (ok, error, items); never throws.</summary>
    public Task<(bool ok, string error, List<WorkshopItem> items)> SearchAsync(string query, int count = 30)
        => WorkshopApi.SearchAsync(Cfg.SteamApiKey, query, count);

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
