using System.Text.RegularExpressions;

namespace Dzl.Core.Build.Preflight;

/// <summary>Path plumbing shared by preflight rules: reference normalization (the engine's path
/// dialect), exclusion matching, prefix-file reading, and multi-candidate reference resolution. Pure
/// except the explicit file probes in <see cref="Resolve"/> / <see cref="ReadPrefix"/>.</summary>
public static class PathResolver
{
    private static readonly string[] PrefixFileNames =
        { "$PBOPREFIX$", "$PREFIX$", "$PBOPREFIX$.txt", "$PREFIX$.txt" };

    /// <summary>First non-empty line of the mod's prefix file ($PBOPREFIX$ et al.), normalized to
    /// backslashes with no leading/trailing separators. "" when absent/empty.</summary>
    public static string ReadPrefix(string modDir)
    {
        foreach (var name in PrefixFileNames)
        {
            var path = Path.Combine(modDir, name);
            if (!File.Exists(path)) continue;
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var v = line.Trim().Trim('"', '\'');
                    if (v.Length > 0) return v.Replace('/', '\\').Trim('\\');
                }
            }
            catch { return ""; }
        }
        return "";
    }

    /// <summary>All prefix files present (to flag duplicates).</summary>
    public static List<string> PrefixFiles(string modDir) =>
        PrefixFileNames.Select(n => Path.Combine(modDir, n)).Where(File.Exists).ToList();

    /// <summary>Normalize an engine path reference: trim quotes/space, strip a Workbench
    /// <c>{GUID}</c> resource prefix, forward→back slashes, no leading separators.</summary>
    public static string NormalizeRef(string reference)
    {
        var v = (reference ?? "").Trim().Trim('"', '\'');
        var m = Regex.Match(v, @"^\{[0-9A-Fa-f]{8,32}\}(.+)$");
        if (m.Success) v = m.Groups[1].Value.Trim();
        v = v.Replace('/', '\\');
        return v.TrimStart('\\');
    }

    /// <summary>True when the reference is an absolute drive path (<c>P:\...</c>, <c>C:/...</c>) —
    /// a baked dev-machine path that breaks on every other machine once packed.</summary>
    public static bool IsAbsoluteDrivePath(string reference)
    {
        var v = (reference ?? "").Trim().Trim('"', '\'');
        return v.Length >= 3 && char.IsLetter(v[0]) && v[1] == ':' && (v[2] == '\\' || v[2] == '/');
    }

    /// <summary>Simple glob match (<c>*</c>/<c>?</c>) against one path segment, case-insensitive.</summary>
    public static bool MatchesPattern(string name, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        var rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase);
    }

    /// <summary>True when any segment of <paramref name="relPath"/> matches an exclude pattern —
    /// i.e. this path would not (or should not) ship in the PBO.</summary>
    public static bool IsExcluded(string relPath, IReadOnlyList<string> patterns)
    {
        var segments = NormalizeRef(relPath).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(seg => patterns.Any(p => MatchesPattern(seg, p)));
    }

    /// <summary>Combined default + extra exclude patterns for an options set.</summary>
    public static string[] EffectiveExcludes(PreflightOptions opts) =>
        PreflightOptions.DefaultExcludes.Concat(opts.ExcludePatterns).ToArray();

    /// <summary>Resolve an engine reference to a file on disk. Returns the first hit, or
    /// (firstCandidate, false).</summary>
    /// <remarks>Candidates, in order: the mod dir, the mod's parent (sibling-addon refs), prefix-relative
    /// (a reference starting with the mod's prefix or folder name maps into the mod dir), and the
    /// work-drive root (vanilla refs like <c>dz\gear\...</c>).</remarks>
    public static (string path, bool found) Resolve(string reference, string modDir, string prefix,
        string? workDriveRoot)
    {
        var r = NormalizeRef(reference);
        if (r.Length == 0) return ("", false);

        var candidates = new List<string>();
        var refOs = r.Replace('\\', Path.DirectorySeparatorChar);

        if (IsAbsoluteDrivePath(reference))
            candidates.Add((reference ?? "").Trim().Trim('"', '\''));

        modDir = Path.GetFullPath(modDir);
        candidates.Add(Path.Combine(modDir, refOs));
        candidates.Add(Path.Combine(Path.GetDirectoryName(modDir) ?? modDir, refOs));

        // "MyMod\data\x.paa" (or "<prefix-first-segment>\...") → strip the first segment, map into the mod dir.
        var parts = r.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var folderName = Path.GetFileName(modDir);
        var prefixParts = prefix.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var prefixFirst = prefixParts.FirstOrDefault() ?? "";
        if (parts.Length > 1 &&
            (parts[0].Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
             (prefixFirst.Length > 0 && parts[0].Equals(prefixFirst, StringComparison.OrdinalIgnoreCase))))
            candidates.Add(Path.Combine(modDir, Path.Combine(parts[1..])));

        // Multi-segment prefix (e.g. $PBOPREFIX$ "DemoPack\Core"): a reference that opens with the WHOLE
        // prefix maps to the mod dir after the full prefix, not just the first segment.
        if (prefixParts.Length > 1 && parts.Length > prefixParts.Length &&
            parts.Take(prefixParts.Length).SequenceEqual(prefixParts, StringComparer.OrdinalIgnoreCase))
            candidates.Add(Path.Combine(modDir, Path.Combine(parts[prefixParts.Length..])));

        if (!string.IsNullOrEmpty(workDriveRoot))
            candidates.Add(Path.Combine(workDriveRoot, refOs));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? first = null;
        foreach (var c in candidates)
        {
            string full;
            try { full = Path.GetFullPath(c); } catch { continue; }
            if (!seen.Add(full)) continue;
            first ??= full;
            if (File.Exists(full) || Directory.Exists(full)) return (full, true);
        }
        return (first ?? r, false);
    }

    /// <summary>Relative path of <paramref name="path"/> under <paramref name="baseDir"/> with
    /// backslashes, or null when it's outside.</summary>
    public static string? RelativeTo(string path, string baseDir)
    {
        try
        {
            var rel = Path.GetRelativePath(baseDir, path);
            if (rel == "." || rel.StartsWith("..") || Path.IsPathRooted(rel)) return null;
            return rel.Replace(Path.DirectorySeparatorChar, '\\');
        }
        catch { return null; }
    }
}
