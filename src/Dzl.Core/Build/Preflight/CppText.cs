using System.Text;
using System.Text.RegularExpressions;

namespace Dzl.Core.Build.Preflight;

/// <summary>One <c>class Name : Base { body }</c> block found in config text.</summary>
public sealed record CppClassBlock(string Name, string Base, string Body, int StartIndex, int EndIndex);

/// <summary>Minimal, string-aware text utilities for Bohemia config syntax
/// (<c>config.cpp</c>/<c>.hpp</c>). Not a real parser — just enough structure (comments, strings,
/// braces, arrays, includes) for preflight rules to reason about configs without choking on comments or
/// quoted braces.</summary>
/// <remarks>All pure; file I/O only in <see cref="ReadWithIncludes"/> via an injectable reader.</remarks>
public static class CppText
{
    /// <summary>Remove <c>//</c> and <c>/* */</c> comments without touching string literals.
    /// With <paramref name="preserveLines"/> the removed text becomes spaces/newlines so
    /// character indexes still map to the original line numbers.</summary>
    public static string StripComments(string content, bool preserveLines = false)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var sb = new StringBuilder(content.Length);
        int i = 0;
        char inString = '\0';
        bool escaped = false;

        while (i < content.Length)
        {
            char c = content[i];
            char next = i + 1 < content.Length ? content[i + 1] : '\0';

            if (inString != '\0')
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == inString) inString = '\0';
                i++;
                continue;
            }
            if (c is '"' or '\'') { inString = c; sb.Append(c); i++; continue; }
            if (c == '/' && next == '/')
            {
                i += 2;
                while (i < content.Length && content[i] is not ('\r' or '\n'))
                {
                    if (preserveLines) sb.Append(' ');
                    i++;
                }
                continue;
            }
            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < content.Length)
                {
                    if (content[i] == '*' && i + 1 < content.Length && content[i + 1] == '/') { i += 2; break; }
                    if (preserveLines) sb.Append(content[i] is '\r' or '\n' ? content[i] : ' ');
                    i++;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Index of the <c>}</c> matching the <c>{</c> at <paramref name="openIndex"/>,
    /// skipping braces inside string literals. -1 when unbalanced.</summary>
    public static int FindMatchingBrace(string content, int openIndex)
    {
        int depth = 0;
        char inString = '\0';
        bool escaped = false;
        for (int i = openIndex; i < content.Length; i++)
        {
            char c = content[i];
            if (inString != '\0')
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == inString) inString = '\0';
                continue;
            }
            if (c is '"' or '\'') { inString = c; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static readonly Regex ClassBlockRegex = new(
        @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([A-Za-z_][A-Za-z0-9_]*))?\s*\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>All <c>class X [: Base] { ... }</c> blocks (at any nesting depth, in document order).
    /// Expects comment-stripped input.</summary>
    public static IEnumerable<CppClassBlock> ClassBlocks(string content)
    {
        int pos = 0;
        while (true)
        {
            var m = ClassBlockRegex.Match(content, pos);
            if (!m.Success) yield break;
            int open = content.IndexOf('{', m.Index);
            int close = FindMatchingBrace(content, open);
            if (close < 0) { pos = m.Index + m.Length; continue; }
            yield return new CppClassBlock(m.Groups[1].Value, m.Groups[2].Value,
                content.Substring(open + 1, close - open - 1), m.Index, close + 1);
            pos = open + 1;   // descend into the body so nested classes are visited too
        }
    }

    /// <summary>Body of the first <c>class &lt;name&gt; { ... }</c> (case-insensitive), or "" when absent.</summary>
    public static string FindClassBody(string content, string className)
    {
        var m = Regex.Match(content, @"\bclass\s+" + Regex.Escape(className) + @"\b[^;{]*\{", RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        int open = content.IndexOf('{', m.Index);
        int close = FindMatchingBrace(content, open);
        return close < 0 ? "" : content.Substring(open + 1, close - open - 1);
    }

    /// <summary>Values of <c>name[] = {"a","b"};</c> (or <c>+=</c>). Null when the array is absent,
    /// empty list when declared empty. Whitespace/quotes trimmed per item.</summary>
    public static List<string>? ParseArrayValues(string content, string arrayName)
    {
        var m = Regex.Match(content,
            @"\b" + Regex.Escape(arrayName) + @"\s*\[\s*\]\s*\+?=\s*\{(.*?)\}\s*;",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        return m.Groups[1].Value.Split(',')
            .Select(s => s.Trim().Trim('"', '\''))
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>1-based line number of a character index (for findings). 0 for invalid input.</summary>
    public static int LineOf(string content, int index)
    {
        if (content is null || index < 0) return 0;
        int line = 1;
        int end = Math.Min(index, content.Length);
        for (int i = 0; i < end; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    private static readonly Regex IncludeRegex = new(
        @"^\s*#include\s+[""<]([^"">]+)["">]", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>All <c>#include</c> values (raw, in order) in comment-stripped content.</summary>
    public static List<string> IncludeValues(string content) =>
        IncludeRegex.Matches(content).Select(m => m.Groups[1].Value.Trim()).ToList();

    /// <summary>Read a config file and inline its <c>#include</c>s recursively (comment-stripped,
    /// line-preserving). <paramref name="resolveInclude"/> maps an include value + the including
    /// file's path to an absolute path, or null when unresolvable (the directive is left as-is).
    /// Cycle-safe; unreadable files yield "".</summary>
    public static string ReadWithIncludes(string path, Func<string, string, string?> resolveInclude,
        HashSet<string>? seen = null)
    {
        seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string key;
        try { key = Path.GetFullPath(path); } catch { key = path; }
        if (!seen.Add(key)) return "";

        string raw;
        try { raw = File.ReadAllText(path); } catch { return ""; }

        var content = StripComments(raw, preserveLines: true);
        return IncludeRegex.Replace(content, m =>
        {
            var resolved = resolveInclude(m.Groups[1].Value.Trim(), path);
            return resolved is null ? m.Value : ReadWithIncludes(resolved, resolveInclude, seen);
        });
    }
}
