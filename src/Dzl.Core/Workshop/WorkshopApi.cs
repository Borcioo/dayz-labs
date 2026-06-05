using System.Text.Json;

namespace Dzl.Core.Workshop;

/// <summary>A Workshop item: id + title plus the browse/detail metadata (preview image, subs, dates, tags,
/// short description) the Web API returns when asked for it.</summary>
public sealed record WorkshopItem(
    string Id,
    string Title,
    string Description = "",
    long Updated = 0,
    string PreviewUrl = "",
    long Subscriptions = 0,
    long Created = 0,
    string Tags = "");

/// <summary>Steam Workshop browsing via the Steam Web API (<c>IPublishedFileService/QueryFiles</c>). Needs a
/// Web API key. URL building + JSON parsing are pure + unit-tested; the HTTP calls are thin and never throw.</summary>
public static class WorkshopApi
{
    /// <summary>DayZ's Steam app id — Workshop items live under it.</summary>
    public const string AppId = "221100";

    /// <summary>Browse mode → EPublishedFileQueryType: top=0 (RankedByVote), recent=1 (RankedByPublicationDate),
    /// trending=3 (RankedByTrend), search=12 (RankedByTextSearch).</summary>
    public static int QueryType(string mode) => mode switch
    {
        "recent" => 1,
        "trending" => 3,
        "search" => 12,
        _ => 0,   // top
    };

    /// <summary>Build a QueryFiles URL for any query type, page, and optional required tag (category filter).</summary>
    public static string QueryUrl(string apiKey, int queryType, string query, int count, int page = 1, string tag = "")
    {
        var n = count is > 0 and <= 100 ? count : 30;
        var url = "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/"
             + $"?key={Uri.EscapeDataString(apiKey)}"
             + $"&appid={AppId}"
             + $"&query_type={queryType}"
             + $"&search_text={Uri.EscapeDataString(query)}"
             + $"&numperpage={n}&page={(page > 0 ? page : 1)}"
             + "&return_metadata=true&return_previews=true&return_tags=true"
             + "&return_vote_data=true&return_short_description=true";
        if (!string.IsNullOrWhiteSpace(tag)) url += $"&requiredtags%5B0%5D={Uri.EscapeDataString(tag)}";
        return url;
    }

    /// <summary>Text-search URL (query_type 12) — kept for callers/tests.</summary>
    public static string SearchUrl(string apiKey, string query, int count) => QueryUrl(apiKey, 12, query, count);

    private static string TagsCsv(JsonElement e)
    {
        if (!e.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array) return "";
        var names = tags.EnumerateArray()
            .Select(t => t.TryGetProperty("display_name", out var dn) ? dn.GetString()
                       : t.TryGetProperty("tag", out var tg) ? tg.GetString() : null)
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(", ", names);
    }

    private static long Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    /// <summary>Parse a QueryFiles response into items (pure). Tolerates missing fields.</summary>
    public static List<WorkshopItem> ParseSearch(string json)
    {
        var items = new List<WorkshopItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var resp)) return items;
        if (!resp.TryGetProperty("publishedfiledetails", out var arr) || arr.ValueKind != JsonValueKind.Array) return items;
        foreach (var e in arr.EnumerateArray())
        {
            var id = Str(e, "publishedfileid");
            if (id.Length == 0) continue;
            items.Add(new WorkshopItem(
                id,
                Str(e, "title"),
                Str(e, "short_description"),
                Long(e, "time_updated"),
                Str(e, "preview_url"),
                Long(e, "subscriptions"),
                Long(e, "time_created"),
                TagsCsv(e)));
        }
        return items;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Browse the Workshop by mode (top/recent/trending/search), page, and optional category tag.
    /// Returns (ok, error, items).</summary>
    public static async Task<(bool ok, string error, List<WorkshopItem> items)> BrowseAsync(
        string apiKey, string mode, string query = "", int count = 30, int page = 1, string tag = "")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "no Steam Web API key — set it in Settings (steamcommunity.com/dev/apikey)", new());
        try
        {
            var json = await Http.GetStringAsync(QueryUrl(apiKey, QueryType(mode), query, count, page, tag)).ConfigureAwait(false);
            return (true, "", ParseSearch(json));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, new());
        }
    }

    /// <summary>Text search (mode "search"). Kept for existing callers.</summary>
    public static Task<(bool ok, string error, List<WorkshopItem> items)> SearchAsync(string apiKey, string query, int count = 30)
        => BrowseAsync(apiKey, "search", query, count);
}
