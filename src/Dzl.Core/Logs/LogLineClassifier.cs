namespace Dzl.Core.Logs;

/// <summary>Which quick-filter bucket(s) a log line falls into. Flags so a line can be both
/// (e.g. an error mentioning a connection); the pane's filter checks a single bucket via HasFlag.</summary>
[Flags]
public enum LogCategory
{
    None = 0,
    Error = 1,
    Warning = 2,
    Connection = 4,
    ModSuccess = 8,
}

/// <summary>Pure, substring-based classification of DayZ log lines into the quick-filter buckets
/// (Errors / Warnings / Connections / Mods·Success) plus the combined filter+search predicate the
/// log panes use. Heuristics on purpose: a quick filter only needs to be useful, not exact.</summary>
public static class LogLineClassifier
{
    private static bool Has(string line, params string[] needles)
    {
        foreach (var n in needles)
            if (line.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Bucket(s) for a single line. Engine/script errors, <c>(W):</c> warnings, player
    /// connect/disconnect, and positive mod/mission load events; neutral lines return <see cref="LogCategory.None"/>.</summary>
    public static LogCategory Classify(string line)
    {
        var cat = LogCategory.None;
        if (Has(line, "(e):", "error", "exception", "can't compile", "cannot ", "failed")) cat |= LogCategory.Error;
        if (Has(line, "(w):", "warning")) cat |= LogCategory.Warning;
        if (Has(line, "connect")) cat |= LogCategory.Connection;           // connected / disconnected / connection
        if (Has(line, "loading mod", "loaded", "successfully", "mission read", "initializing", "registered"))
            cat |= LogCategory.ModSuccess;
        return cat;
    }

    /// <summary>Map a UI filter key to its bucket; "all" and anything unrecognised mean "no bucket
    /// restriction" (<see cref="LogCategory.None"/>).</summary>
    private static LogCategory FilterCategory(string filter) => filter switch
    {
        "errors" => LogCategory.Error,
        "warnings" => LogCategory.Warning,
        "connections" => LogCategory.Connection,
        "mods" => LogCategory.ModSuccess,
        _ => LogCategory.None,
    };

    /// <summary>True when a line should be shown for the given quick-filter and search box: it must be
    /// in the filter's bucket (or the filter is "all") AND contain the search text (case-insensitive,
    /// empty search matches everything).</summary>
    public static bool Matches(string line, string filter, string search)
    {
        if (!string.IsNullOrEmpty(search) && !line.Contains(search, StringComparison.OrdinalIgnoreCase))
            return false;
        var cat = FilterCategory(filter);
        return cat == LogCategory.None || Classify(line).HasFlag(cat);
    }
}
