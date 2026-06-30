using System.Text.RegularExpressions;

namespace Dzl.Core.Logs;

/// <summary>Token kinds a log line is coloured by. <see cref="LogToken.Default"/> is the uncoloured gaps
/// between spans (the viewer's normal foreground).</summary>
public enum LogToken
{
    Default,
    Timestamp,
    Subsystem,
    Error,
    Warning,
    FileRef,
    Quoted,
    Pos,
    KeyValue,
    Success,
}

/// <summary>A coloured region of a line: <paramref name="Start"/>/<paramref name="Length"/> are character
/// offsets into the line.</summary>
public readonly record struct LogSpan(int Start, int Length, LogToken Token);

/// <summary>Pure, regex-based tokenizer for DayZ log lines (RPT / script / ADM / console). Produces sorted,
/// non-overlapping coloured spans for the viewer's syntax highlighter, and resolves a clickable file:line
/// reference at a column (stack-trace "…/foo.c : 339" → open that file at that line). Heuristic, like the
/// quick-filter classifier: a miss just leaves the text default-coloured, never throws.</summary>
public static partial class LogSyntax
{
    // Absolute Windows path (drive + slashes, no spaces/colons) OR a relative path, ending in .c/.cpp,
    // with an optional " : NNN" line number. Used both for colouring and click-to-open.
    [GeneratedRegex(@"(?<path>[A-Za-z]:[\\/][^\s:]*\.(?:c|cpp)|[\w./\\-]+\.(?:c|cpp))(?:\s*:\s*(?<line>\d+))?")]
    private static partial Regex FileRefRx();

    [GeneratedRegex(@"^\s*\d{1,2}:\d{2}:\d{2}(?:\.\d+)?")]
    private static partial Regex TimestampRx();

    [GeneratedRegex(@"\((E|W)\)")]
    private static partial Regex SeverityRx();

    [GeneratedRegex(@"\b(?:ENGINE|RESOURCES|ENTITY|SCRIPT|NETWORK|MENU|SOUND|PHYSX|ANIMATION|WORLD|CRAFTING|STATE|SYSTEM|CONFIG|PROTOCOL)\b|\[[A-Za-z][\w]*\]")]
    private static partial Regex SubsystemRx();

    [GeneratedRegex("'[^']*'|\"[^\"]*\"")]
    private static partial Regex QuotedRx();

    [GeneratedRegex(@"pos=<[^>]*>")]
    private static partial Regex PosRx();

    [GeneratedRegex(@"\bid=[^\s)]+")]
    private static partial Regex KeyValueRx();

    [GeneratedRegex(@"\b(?:SUCCESS|successfully)\b")]
    private static partial Regex SuccessRx();

    // Lower number = higher priority; on overlap the higher-priority span wins.
    private static readonly (Func<Regex> Rx, LogToken Token, int Priority)[] Rules =
    {
        (TimestampRx, LogToken.Timestamp, 1),
        (SeverityRx,  LogToken.Error,     2),   // Error/Warning split out below by the matched letter
        (FileRefRx,   LogToken.FileRef,   3),
        (QuotedRx,    LogToken.Quoted,    4),
        (PosRx,       LogToken.Pos,       5),
        (KeyValueRx,  LogToken.KeyValue,  6),
        (SuccessRx,   LogToken.Success,   7),
        (SubsystemRx, LogToken.Subsystem, 8),
    };

    /// <summary>Sorted, non-overlapping coloured spans for a line. Gaps between spans are
    /// <see cref="LogToken.Default"/> and left to the caller's normal foreground.</summary>
    public static IReadOnlyList<LogSpan> Tokenize(string line)
    {
        if (string.IsNullOrEmpty(line)) return Array.Empty<LogSpan>();

        var candidates = new List<(LogSpan Span, int Priority)>();
        foreach (var (rxFactory, token, priority) in Rules)
        {
            foreach (Match m in rxFactory().Matches(line))
            {
                if (m.Length == 0) continue;
                var tok = token == LogToken.Error
                    ? (m.Value.Contains('W') ? LogToken.Warning : LogToken.Error)
                    : token;
                candidates.Add((new LogSpan(m.Index, m.Length, tok), priority));
            }
        }

        // Greedy by start, then priority: accept the earliest span, skip anything overlapping it.
        candidates.Sort((a, b) =>
            a.Span.Start != b.Span.Start ? a.Span.Start - b.Span.Start : a.Priority - b.Priority);

        var result = new List<LogSpan>();
        var nextFree = 0;
        foreach (var (span, _) in candidates)
        {
            if (span.Start < nextFree) continue;   // overlaps an accepted span
            result.Add(span);
            nextFree = span.Start + span.Length;
        }
        return result;
    }

    /// <summary>If <paramref name="column"/> falls inside a "…/foo.c[ : NNN]" reference, return the path and
    /// line (0 when absent). Used to open the file in the editor on click. Null when not on a reference.</summary>
    public static (string Path, int Line)? FileRefAt(string line, int column)
    {
        if (string.IsNullOrEmpty(line)) return null;
        foreach (Match m in FileRefRx().Matches(line))
        {
            if (column < m.Index || column > m.Index + m.Length) continue;
            var path = m.Groups["path"].Value;
            var lineNo = m.Groups["line"].Success ? int.Parse(m.Groups["line"].Value) : 0;
            return (path, lineNo);
        }
        return null;
    }
}
