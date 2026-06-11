namespace Dzl.Core.Build.Preflight.Rules;

/// <summary>
/// Filesystem-layer checks over the packable payload: case conflicts, risky path characters,
/// the official lowercase rule (Linux server binaries), texture freshness, already-binarized
/// ODOL models, dev-only files that would ship, and mod.cpp presentation sanity.
/// </summary>
public static class FileSystemRules
{
    private static readonly char[] InvalidNameChars = { '<', '>', '"', '|', '?', '*' };
    private static readonly string[] SourceTextureExts = { ".png", ".tga" };

    public static void Check(string modDir, string prefix, PreflightOptions opts, PreflightReport report)
    {
        var excludes = PathResolver.EffectiveExcludes(opts);
        var byLower = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasUppercase = new List<string>();

        foreach (var file in Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories))
        {
            var rel = PathResolver.RelativeTo(file, modDir);
            if (rel is null) continue;
            var excluded = PathResolver.IsExcluded(rel, excludes);

            if (!excluded)
            {
                CheckPathCharacters(rel, file, report);
                CheckCaseConflict(rel, byLower, report);
                if (rel.Any(char.IsUpper)) hasUppercase.Add(rel);
                CheckOdol(file, rel, report);
                CheckDevFile(rel, report);
            }

            if (SourceTextureExts.Contains(Path.GetExtension(file).ToLowerInvariant()))
                CheckTextureFreshness(file, rel, report);
        }

        if (hasUppercase.Count > 0)
            report.Warn("path-uppercase",
                $"{hasUppercase.Count} packed path(s) contain uppercase characters (e.g. {hasUppercase[0]}). " +
                "Official guidance: keep packed files lowercase or the mod breaks on DayZ Linux Server binaries.");

        if (opts.CheckModCpp) CheckModCpp(modDir, prefix, opts, report);
    }

    private static void CheckPathCharacters(string rel, string fullPath, PreflightReport report)
    {
        var name = Path.GetFileName(rel);
        if (name.Any(c => c < 32))
            report.Warn("path-control-chars", $"Path contains control characters: {rel}", rel);
        if (name.IndexOfAny(InvalidNameChars) >= 0)
            report.Warn("path-invalid-chars", $"Path contains Windows-invalid characters: {rel}", rel);
        if (name != name.Trim())
            report.Warn("path-whitespace", $"Path has leading/trailing whitespace: {rel}", rel);
        if (rel.Any(c => c > 127))
            report.Warn("path-non-ascii",
                $"Path contains non-ASCII characters: {rel} — PBO entry names must encode to ASCII.", rel);
        if (fullPath.Length > 240)
            report.Warn("path-too-long", $"Absolute path exceeds 240 chars and may break the tools: {rel}", rel);
    }

    private static void CheckCaseConflict(string rel, Dictionary<string, string> byLower, PreflightReport report)
    {
        var key = rel.ToLowerInvariant();
        if (byLower.TryGetValue(key, out var existing) && existing != rel)
            report.Warn("path-case-conflict",
                $"Case-only path conflict: {existing} <-> {rel} — case-fuzzy on Windows, two files on Linux.", rel);
        else
            byLower[key] = rel;
    }

    private static void CheckOdol(string file, string rel, PreflightReport report)
    {
        if (!rel.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase)) return;
        Span<byte> magic = stackalloc byte[4];
        try
        {
            using var fs = File.OpenRead(file);
            if (fs.Read(magic) < 4) return;
        }
        catch { return; }
        if (magic.SequenceEqual("ODOL"u8))
            report.Warn("p3d-odol",
                $"P3D is already binarized (ODOL): {rel} — feeding it to Binarize crashes with an access violation (0xC0000005). Pack it without re-binarizing.",
                rel);
    }

    private static void CheckDevFile(string rel, PreflightReport report)
    {
        // Files that *should* be excluded but aren't covered by the exclude list because the
        // user disabled defaults are caught by IsExcluded upstream; here we only flag the
        // suspicious leftovers that pattern-matching can't classify (source art next to .paa
        // is handled by texture freshness).
        var name = Path.GetFileName(rel).ToLowerInvariant();
        if (name.EndsWith(".pbo"))
            report.Warn("payload-nested-pbo", $"A .pbo inside the source tree would be packed into the new PBO: {rel}", rel);
    }

    private static void CheckTextureFreshness(string sourceTexture, string rel, PreflightReport report)
    {
        var paa = Path.ChangeExtension(sourceTexture, ".paa");
        if (!File.Exists(paa))
        {
            report.Warn("texture-no-paa",
                $"Source texture has no matching .paa: {rel} — run ImageToPAA (or dzl convert-paa) before packing.", rel);
            return;
        }
        try
        {
            if (File.GetLastWriteTimeUtc(sourceTexture) > File.GetLastWriteTimeUtc(paa))
                report.Warn("texture-stale-paa",
                    $"Source texture is newer than its .paa: {rel} — the packed mod ships the OLD texture.", rel);
        }
        catch { }
    }

    /// <summary>mod.cpp lives next to Addons/ in the deployed @Mod (outside the PBO) but our
    /// projects keep it in the source root; validate presentation fields + image paths when present.</summary>
    private static void CheckModCpp(string modDir, string prefix, PreflightOptions opts, PreflightReport report)
    {
        var modCpp = Path.Combine(modDir, "mod.cpp");
        if (!File.Exists(modCpp)) return;

        string content;
        try { content = CppText.StripComments(File.ReadAllText(modCpp), preserveLines: true); }
        catch { return; }

        foreach (var field in new[] { "name", "author" })
            if (!System.Text.RegularExpressions.Regex.IsMatch(content, $@"\b{field}\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                report.Info("modcpp-field", $"mod.cpp has no '{field}' — shown in the in-game mod list.", "mod.cpp");

        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(content,
            @"\b(picture|logo|logoSmall|logoOver)\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var (resolved, found) = PathResolver.Resolve(m.Groups[2].Value, modDir, prefix, opts.WorkDriveRoot);
            if (!found)
                report.Warn("modcpp-image-missing",
                    $"mod.cpp {m.Groups[1].Value} points at a missing file: {m.Groups[2].Value}",
                    "mod.cpp", CppText.LineOf(content, m.Index));
        }
    }
}
