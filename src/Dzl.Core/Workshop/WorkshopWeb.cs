using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dzl.Core.Workshop;

/// <summary>A Workshop item: id + title plus the browse/detail metadata (preview image, subs, dates, size,
/// tags, short description) the keyless endpoints return.</summary>
public sealed record WorkshopItem(
    string Id,
    string Title,
    string Description = "",
    long Updated = 0,
    string PreviewUrl = "",
    long Subscriptions = 0,
    long Created = 0,
    string Tags = "",
    long FileSize = 0)
{
    /// <summary>Human file size (e.g. "48.2 MB"), or "" when unknown. Invariant decimal point.</summary>
    public string SizeText => FileSize <= 0 ? "" :
        FileSize >= 1L << 20
            ? (FileSize / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB"
            : (FileSize / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";

    /// <summary>Posted date (local, yyyy-MM-dd), or "".</summary>
    public string CreatedText => Created <= 0 ? "" : DateTimeOffset.FromUnixTimeSeconds(Created).LocalDateTime.ToString("yyyy-MM-dd");

    /// <summary>Last-updated date (local, yyyy-MM-dd), or "".</summary>
    public string UpdatedText => Updated <= 0 ? "" : DateTimeOffset.FromUnixTimeSeconds(Updated).LocalDateTime.ToString("yyyy-MM-dd");
}

/// <summary>A sort option as the Workshop browse page exposes it (label + its <c>browsesort</c> value).</summary>
public sealed record WorkshopSort(string Label, string BrowseSort);

/// <summary>A "Most Popular" time window (label + <c>days</c>; -1 = all time).</summary>
public sealed record WorkshopTimeFrame(string Label, int Days);

/// <summary>Keyless Workshop browsing by scraping the Steam Community browse page — full filters (sort,
/// time frame, DayZ Mod-Type tags, text search, pagination) server-side, no Web API key. URL/HTML/JSON
/// helpers are pure + unit-tested; fetches are thin.</summary>
/// <remarks>Steam's markup uses hashed CSS classes, so the list parser keys off the stable
/// <c>filedetails/?id=N"&gt;&lt;img src=preview alt=title&gt;</c> structure. Per-item detail
/// (subscribers/description/tags) is fetched via the keyless <c>GetPublishedFileDetails</c>
/// endpoint.</remarks>
public static class WorkshopWeb
{
    public const string AppId = "221100";

    /// <summary>Sort options matching the Workshop dropdown.</summary>
    public static readonly IReadOnlyList<WorkshopSort> Sorts = new[]
    {
        new WorkshopSort("Most Popular", "trend"),
        new WorkshopSort("Top Rated", "toprated"),
        new WorkshopSort("Most Recent", "mostrecent"),
        new WorkshopSort("Last Updated", "lastupdated"),
        new WorkshopSort("Most Subscribed", "totaluniquesubscribers"),
    };

    /// <summary>Time windows for "Most Popular" (browsesort=trend &amp; days=N).</summary>
    public static readonly IReadOnlyList<WorkshopTimeFrame> TimeFrames = new[]
    {
        new WorkshopTimeFrame("Today", 1), new WorkshopTimeFrame("One Week", 7),
        new WorkshopTimeFrame("Thirty Days", 30), new WorkshopTimeFrame("Three Months", 90),
        new WorkshopTimeFrame("Six Months", 180), new WorkshopTimeFrame("One Year", 365),
        new WorkshopTimeFrame("All Time", -1),
    };

    /// <summary>DayZ "Mod Type" category tags (requiredtags) — from the Workshop browse sidebar.</summary>
    public static readonly IReadOnlyList<string> ModTypes = new[]
    {
        "Animation", "Character", "Economy", "Environment", "Equipment", "Mechanics",
        "Sound", "Props", "Terrain", "Vehicle", "Weapon",
    };

    /// <summary>"Type" tags.</summary>
    public static readonly IReadOnlyList<string> Types = new[] { "Mod", "Server" };

    public static string BrowseUrl(string browseSort, int days, string query, int page, IEnumerable<string>? tags = null)
    {
        var sort = string.IsNullOrWhiteSpace(browseSort) ? "trend" : browseSort;
        var p = page > 0 ? page : 1;
        var url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&section=readytouseitems"
                + $"&browsesort={sort}&actualsort={sort}&p={p}";
        if (sort == "trend" && days != 0) url += $"&days={days}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&searchtext={Uri.EscapeDataString(query)}";
        foreach (var t in (tags ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
            url += $"&requiredtags%5B%5D={Uri.EscapeDataString(t)}";
        return url;
    }

    // <a href="…/filedetails/?id=123" class="hashed"><img src="preview" alt="Title" …>
    private static readonly Regex ItemRx = new(
        @"filedetails/\?id=(\d+)""[^>]*>\s*<img\s+src=""([^""]+)""\s+alt=""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parse browse-page HTML into items (id, title from img alt, preview). Deduped, order preserved. Pure.</summary>
    public static List<WorkshopItem> ParseBrowse(string html)
    {
        var items = new List<WorkshopItem>();
        var seen = new HashSet<string>();
        foreach (Match m in ItemRx.Matches(html))
        {
            var id = m.Groups[1].Value;
            if (!seen.Add(id)) continue;
            items.Add(new WorkshopItem(id,
                WebUtility.HtmlDecode(m.Groups[3].Value).Trim(),
                PreviewUrl: WebUtility.HtmlDecode(m.Groups[2].Value)));
        }
        return items;
    }

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (dzl)");
        return c;
    }

    /// <summary>Browse with full filters (keyless). Returns (ok, error, items); never throws.</summary>
    public static async Task<(bool ok, string error, List<WorkshopItem> items)> BrowseAsync(
        string browseSort, int days, string query, int count, int page, IEnumerable<string>? tags = null)
    {
        try
        {
            var html = await Http.GetStringAsync(BrowseUrl(browseSort, days, query, page, tags)).ConfigureAwait(false);
            var items = ParseBrowse(html);
            if (count > 0 && items.Count > count) items = items.Take(count).ToList();
            return (true, "", items);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, new());
        }
    }

    /// <summary>Parse a GetPublishedFileDetails response into one item (pure). Null if absent.</summary>
    public static WorkshopItem? ParseDetails(string json, string id)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var resp)) return null;
        if (!resp.TryGetProperty("publishedfiledetails", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var e = arr.EnumerateArray().FirstOrDefault();
        if (e.ValueKind != JsonValueKind.Object) return null;

        string S(string n) => e.TryGetProperty(n, out var v) ? v.GetString() ?? "" : "";
        long L(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
        var tags = "";
        if (e.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
            tags = string.Join(", ", tg.EnumerateArray()
                .Select(t => t.TryGetProperty("tag", out var tv) ? tv.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        // file_size is sometimes a JSON number, sometimes a numeric string — handle both.
        long size = L("file_size");
        if (size == 0 && e.TryGetProperty("file_size", out var fs) && fs.ValueKind == JsonValueKind.String
            && long.TryParse(fs.GetString(), out var fsl)) size = fsl;   // numeric string fallback

        var realId = S("publishedfileid");
        return new WorkshopItem(realId.Length > 0 ? realId : id, S("title"), S("file_description"),
            L("time_updated"), S("preview_url"), L("subscriptions"), L("time_created"), tags, size);
    }

    private static byte[] Varint(ulong v)
    {
        var b = new List<byte>();
        do { var x = (byte)(v & 0x7F); v >>= 7; if (v != 0) x |= 0x80; b.Add(x); } while (v != 0);
        return b.ToArray();
    }

    /// <summary>Base64 protobuf body for a Subscribe/Unsubscribe request (CPublishedFile_(Un)Subscribe_Request):
    /// field1 = publishedfileid, field2 = 1, field3 = appid (221100), field4 = 1. Pure — unit-tested against a
    /// captured request.</summary>
    public static string SubscribePayload(string id)
    {
        var b = new List<byte> { 0x08 };
        b.AddRange(Varint(ulong.Parse(id)));
        b.AddRange(new byte[] { 0x10, 0x01, 0x18 });
        b.AddRange(Varint(ulong.Parse(AppId)));
        b.AddRange(new byte[] { 0x20, 0x01 });
        return Convert.ToBase64String(b.ToArray());
    }

    /// <summary>In-app (un)subscribe via IPublishedFileService — needs a Steam web access token (JWT) from a
    /// logged-in session. Returns (ok, message); never throws.</summary>
    public static async Task<(bool ok, string message)> SubscribeAsync(string accessToken, string id, bool subscribe = true)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return (false, "no Steam access token");
        if (string.IsNullOrWhiteSpace(id) || !ulong.TryParse(id, out _)) return (false, "invalid workshop id");
        try
        {
            var verb = subscribe ? "Subscribe" : "Unsubscribe";
            var url = $"https://api.steampowered.com/IPublishedFileService/{verb}/v1?access_token={Uri.EscapeDataString(accessToken)}";
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("input_protobuf_encoded", SubscribePayload(id)),
            });
            var resp = await Http.PostAsync(url, body).ConfigureAwait(false);
            return resp.IsSuccessStatusCode
                ? (true, subscribe ? $"subscribed {id}" : $"unsubscribed {id}")
                : (false, $"HTTP {(int)resp.StatusCode} (token may be expired — re-paste it)");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Fetch full details for one item via the keyless GetPublishedFileDetails endpoint.</summary>
    public static async Task<WorkshopItem?> DetailsAsync(string id)
    {
        try
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("itemcount", "1"),
                new KeyValuePair<string, string>("publishedfileids[0]", id),
            });
            var resp = await Http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseDetails(json, id);
        }
        catch { return null; }
    }
}
