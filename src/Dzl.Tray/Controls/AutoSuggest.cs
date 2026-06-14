namespace Dzl.Tray.Controls;

/// <summary>Pure suggestion-filter shared by <see cref="AutoSuggestBox"/> (and anything else that needs the
/// same "type to search a string pool" behaviour). Extracted so the one filtering rule is unit-tested once.</summary>
public static class AutoSuggest
{
    /// <summary>Case-insensitive substring match of <paramref name="pool"/> against <paramref name="query"/>,
    /// capped at <paramref name="max"/>. A blank query yields nothing — suggestions appear only once the user
    /// starts typing (free text is always allowed by the caller; this only powers the dropdown).</summary>
    public static IEnumerable<string> Filter(IEnumerable<string> pool, string? query, int max)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) yield break;

        var n = 0;
        foreach (var s in pool)
        {
            if (string.IsNullOrEmpty(s) || !s.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            yield return s;
            if (++n >= max) yield break;
        }
    }
}
