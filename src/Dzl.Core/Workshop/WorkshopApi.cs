using System.Text.Json;

namespace Dzl.Core.Workshop;

/// <summary>A Workshop search result: published-file id + friendly title (+ short description / update time).</summary>
public sealed record WorkshopItem(string Id, string Title, string Description = "", long Updated = 0);

/// <summary>Steam Workshop search via the Steam Web API (<c>IPublishedFileService/QueryFiles</c>). Needs a
/// Web API key (no keyless search). URL building + JSON parsing are pure + unit-tested; the HTTP call is thin.</summary>
public static class WorkshopApi
{
    /// <summary>DayZ's Steam app id — Workshop items live under it.</summary>
    public const string AppId = "221100";

    /// <summary>Build the QueryFiles search URL (query_type 12 = ranked-by-text-search, with metadata).</summary>
    public static string SearchUrl(string apiKey, string query, int count)
    {
        var n = count is > 0 and <= 100 ? count : 30;
        return "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/"
             + $"?key={Uri.EscapeDataString(apiKey)}"
             + $"&appid={AppId}"
             + "&query_type=12"
             + $"&search_text={Uri.EscapeDataString(query)}"
             + $"&numperpage={n}&page=1"
             + "&return_metadata=true&return_short_description=true";
    }

    /// <summary>Parse the QueryFiles JSON response into items (pure). Tolerates missing fields.</summary>
    public static List<WorkshopItem> ParseSearch(string json)
    {
        var items = new List<WorkshopItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var resp)) return items;
        if (!resp.TryGetProperty("publishedfiledetails", out var arr) || arr.ValueKind != JsonValueKind.Array) return items;
        foreach (var e in arr.EnumerateArray())
        {
            var id = e.TryGetProperty("publishedfileid", out var pid) ? pid.GetString() ?? "" : "";
            if (id.Length == 0) continue;
            var title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var desc = e.TryGetProperty("short_description", out var d) ? d.GetString() ?? "" : "";
            long updated = e.TryGetProperty("time_updated", out var u) && u.TryGetInt64(out var uv) ? uv : 0;
            items.Add(new WorkshopItem(id, title, desc, updated));
        }
        return items;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Search the Workshop. Returns (ok, error, items); never throws.</summary>
    public static async Task<(bool ok, string error, List<WorkshopItem> items)> SearchAsync(
        string apiKey, string query, int count = 30)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "no Steam Web API key — set it in Settings (steamcommunity.com/dev/apikey)", new());
        try
        {
            var json = await Http.GetStringAsync(SearchUrl(apiKey, query, count)).ConfigureAwait(false);
            return (true, "", ParseSearch(json));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, new());
        }
    }
}
