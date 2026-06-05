using System.Net;
using System.Text.RegularExpressions;

namespace Dzl.Core.Workshop;

/// <summary>Keyless Workshop browsing by scraping the Steam Community workshop browse page (no Web API key
/// needed — what public mod-browser sites effectively do). Steam's markup uses hashed CSS classes, so the
/// parser keys off the stable structure <c>filedetails/?id=N"&gt;&lt;img src="preview" alt="title"&gt;</c>
/// rather than class names. URL building + HTML parsing are pure + unit-tested; the fetch is thin.</summary>
public static class WorkshopWeb
{
    public const string AppId = "221100";

    /// <summary>browsesort value for a mode: top=totaluniquesubscribers, recent=mostrecent, trending=trend,
    /// search=textsearch.</summary>
    public static string Sort(string mode) => mode switch
    {
        "recent" => "mostrecent",
        "trending" => "trend",
        "search" => "textsearch",
        _ => "totaluniquesubscribers",
    };

    public static string BrowseUrl(string mode, string query, int page, string tag = "")
    {
        var p = page > 0 ? page : 1;
        var url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&section=readytouseitems"
                + $"&browsesort={Sort(mode)}&actualsort={Sort(mode)}&p={p}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&searchtext={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(tag)) url += $"&requiredtags%5B%5D={Uri.EscapeDataString(tag)}";
        return url;
    }

    // <a href="…/filedetails/?id=123" class="hashed"><img src="preview" alt="Title" …>
    private static readonly Regex ItemRx = new(
        @"filedetails/\?id=(\d+)""[^>]*>\s*<img\s+src=""([^""]+)""\s+alt=""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parse browse-page HTML into items (id, title from the img alt, preview url). Deduped by id,
    /// order preserved. Pure.</summary>
    public static List<WorkshopItem> ParseBrowse(string html)
    {
        var items = new List<WorkshopItem>();
        var seen = new HashSet<string>();
        foreach (Match m in ItemRx.Matches(html))
        {
            var id = m.Groups[1].Value;
            if (!seen.Add(id)) continue;
            var preview = WebUtility.HtmlDecode(m.Groups[2].Value);
            var title = WebUtility.HtmlDecode(m.Groups[3].Value).Trim();
            items.Add(new WorkshopItem(id, title, PreviewUrl: preview));
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

    /// <summary>Scrape a browse mode + page (top/recent/trending/search). Returns (ok, error, items); never throws.</summary>
    public static async Task<(bool ok, string error, List<WorkshopItem> items)> BrowseAsync(
        string mode, string query = "", int count = 30, int page = 1, string tag = "")
    {
        try
        {
            var html = await Http.GetStringAsync(BrowseUrl(mode, query, page, tag)).ConfigureAwait(false);
            var items = ParseBrowse(html);
            if (count > 0 && items.Count > count) items = items.Take(count).ToList();
            return (true, "", items);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, new());
        }
    }
}
