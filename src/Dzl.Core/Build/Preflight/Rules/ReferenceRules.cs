using System.Text.RegularExpressions;

namespace Dzl.Core.Build.Preflight.Rules;

/// <summary>
/// Reference-layer checks: every quoted asset path in text files must resolve, must not be
/// excluded from the PBO, and must not be an absolute dev-machine path. rvmat textures get a
/// dedicated pass (source-format detection); .p3d binaries get a printable-string scan
/// (warning-level — heuristic by nature).
/// </summary>
public static class ReferenceRules
{
    /// <summary>Text files worth scanning for references.</summary>
    private static readonly string[] TextExtensions =
        { ".cpp", ".hpp", ".h", ".c", ".rvmat", ".cfg", ".xml", ".json", ".layout", ".imageset" };

    /// <summary>Asset extensions that must exist at runtime (missing = broken in game).</summary>
    private const string RiskyExtPattern =
        @"paa|rvmat|p3d|wrp|wss|ogg|wav|edds|emat|ptc|bisurf|imageset|layout";

    private static readonly Regex ReferenceRegex = new(
        $@"[""']([^""']+\.(?:{RiskyExtPattern}))[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RvmatTextureRegex = new(
        @"\btexture\s*=\s*[""]?([^"";\r\n]+\.(?:paa|png|tga|psd|edds))[""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex P3dStringRegex = new(
        @"[A-Za-z0-9_@#$%&()\-+={}\[\],.; \\/]+\.(?:paa|rvmat|edds|emat|ptc|bisurf)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] TextureRoleSuffixes = { "_co", "_nohq", "_smdi", "_as", "_mc", "_ca", "_sm" };

    public static void Check(string modDir, string prefix, PreflightOptions opts, PreflightReport report)
    {
        var excludes = PathResolver.EffectiveExcludes(opts);

        foreach (var file in Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories))
        {
            var rel = PathResolver.RelativeTo(file, modDir);
            if (rel is null || PathResolver.IsExcluded(rel, excludes)) continue;

            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (TextExtensions.Contains(ext))
                ScanTextFile(file, rel, ext, modDir, prefix, opts, excludes, report);
            else if (ext == ".p3d" && opts.CheckP3dStrings)
                ScanP3dStrings(file, rel, modDir, prefix, opts, report);
        }

        CheckHiddenSelectionArity(modDir, prefix, opts, report);
    }

    private static void ScanTextFile(string file, string rel, string ext, string modDir, string prefix,
        PreflightOptions opts, string[] excludes, PreflightReport report)
    {
        string raw;
        try { raw = File.ReadAllText(file); }
        catch (Exception e) { report.Warn("ref-unreadable", $"Could not read for reference scan: {e.Message}", rel); return; }

        report.CheckedFiles++;
        var isCppish = ext is ".cpp" or ".hpp" or ".h" or ".c" or ".cfg" or ".rvmat";
        var content = isCppish ? CppText.StripComments(raw, preserveLines: true) : raw;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReferenceRegex.Matches(content))
        {
            var raw0 = m.Groups[1].Value;
            var line = CppText.LineOf(content, m.Groups[1].Index);

            // Script string concatenation builds paths dynamically — skip "x" + var patterns.
            if (ext == ".c" && IsConcatenated(content, m)) continue;
            // #include is a build-time input, not a runtime reference (config rules handle it).
            if (isCppish && LinePrefixIs(content, m.Index, "#include")) continue;

            var reference = PathResolver.NormalizeRef(raw0);
            if (reference.Length < 5 || !seen.Add(reference)) continue;

            CheckOneReference(raw0, reference, rel, line, "referenced file", FindingSeverity.Error,
                modDir, prefix, opts, excludes, report);
        }

        if (ext == ".rvmat")
            ScanRvmatTextures(content, rel, modDir, prefix, opts, excludes, report, seen);
    }

    private static void CheckOneReference(string rawRef, string reference, string rel, int line,
        string context, FindingSeverity missingSeverity, string modDir, string prefix,
        PreflightOptions opts, string[] excludes, PreflightReport report)
    {
        report.CheckedReferences++;

        if (PathResolver.IsAbsoluteDrivePath(rawRef))
        {
            report.Error("ref-absolute-path",
                $"Absolute drive path baked into {context}: '{rawRef.Trim().Trim('"')}' — resolves only on this machine; use a work-drive-relative path.",
                rel, line);
            return;
        }

        var (resolved, found) = PathResolver.Resolve(reference, modDir, prefix, opts.WorkDriveRoot);
        if (!found)
        {
            var msg = $"Missing {context}: {reference}";
            if (missingSeverity == FindingSeverity.Error) report.Error("ref-missing", msg, rel, line);
            else report.Warn("ref-missing", msg, rel, line);
            return;
        }

        // Exists on disk but matched an exclude pattern → it won't be in the PBO. Nastiest variant.
        var resolvedRel = PathResolver.RelativeTo(resolved, modDir);
        if (resolvedRel is not null && PathResolver.IsExcluded(resolvedRel, excludes))
            report.Error("ref-excluded",
                $"Referenced file exists but is excluded from the PBO: {reference} -> {resolvedRel}", rel, line);
    }

    private static void ScanRvmatTextures(string content, string rel, string modDir, string prefix,
        PreflightOptions opts, string[] excludes, PreflightReport report, HashSet<string> seen)
    {
        foreach (Match m in RvmatTextureRegex.Matches(content))
        {
            var raw0 = m.Groups[1].Value;
            var reference = PathResolver.NormalizeRef(raw0);
            var line = CppText.LineOf(content, m.Groups[1].Index);
            var ext = Path.GetExtension(reference).ToLowerInvariant();

            if (ext is ".png" or ".tga" or ".psd")
                report.Warn("rvmat-source-texture",
                    $"RVMAT references a source texture format instead of .paa: {reference} — works in Workbench, broken in the packed mod.",
                    rel, line);

            if (!seen.Add(reference)) continue;
            CheckOneReference(raw0, reference, rel, line, "RVMAT texture", FindingSeverity.Error,
                modDir, prefix, opts, excludes, report);
        }
    }

    private static void ScanP3dStrings(string file, string rel, string modDir, string prefix,
        PreflightOptions opts, PreflightReport report)
    {
        byte[] data;
        try { data = File.ReadAllBytes(file); }
        catch (Exception e) { report.Warn("ref-unreadable", $"Could not read P3D: {e.Message}", rel); return; }

        report.CheckedFiles++;
        var text = System.Text.Encoding.ASCII.GetString(data);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludes = PathResolver.EffectiveExcludes(opts);

        foreach (Match m in P3dStringRegex.Matches(text))
        {
            var reference = PathResolver.NormalizeRef(m.Value.Trim());
            if (reference.Length < 5 || !seen.Add(reference)) continue;
            // Binary scan is heuristic → warnings, never errors.
            CheckOneReference(m.Value, reference, rel, 0, "internal P3D reference", FindingSeverity.Warning,
                modDir, prefix, opts, excludes, report);
        }
    }

    /// <summary>hiddenSelections[] vs hiddenSelectionsTextures[]/Materials[] count mismatch in any
    /// class — selection without texture (or vice versa) silently no-ops the retexture.</summary>
    private static void CheckHiddenSelectionArity(string modDir, string prefix, PreflightOptions opts,
        PreflightReport report)
    {
        var excludes = PathResolver.EffectiveExcludes(opts);
        foreach (var cfg in ConfigRules.DiscoverConfigs(modDir, excludes))
        {
            var rel = PathResolver.RelativeTo(cfg, modDir) ?? cfg;
            string content;
            try { content = CppText.StripComments(File.ReadAllText(cfg), preserveLines: true); }
            catch { continue; }

            foreach (var block in CppText.ClassBlocks(content))
            {
                var sel = CppText.ParseArrayValues(block.Body, "hiddenSelections");
                var tex = CppText.ParseArrayValues(block.Body, "hiddenSelectionsTextures");
                if (sel is null || tex is null) continue;
                // Nested classes repeat the parent body — only flag when the arrays are in THIS body
                // (ParseArrayValues already scopes to the body, nested duplicates dedupe by message).
                if (tex.Count > sel.Count)
                    report.Warn("hiddensel-arity",
                        $"{block.Name}: hiddenSelectionsTextures[] has {tex.Count} entries but hiddenSelections[] only {sel.Count} — extras never apply.",
                        rel, CppText.LineOf(content, block.StartIndex));
            }
        }
    }

    /// <summary>Texture-role suffix check for shipped texture files (engine assigns the texture
    /// role from the `_co`/`_nohq`/`_smdi` suffix).</summary>
    public static void CheckTextureSuffixes(string modDir, PreflightOptions opts, PreflightReport report)
    {
        var excludes = PathResolver.EffectiveExcludes(opts);
        foreach (var file in Directory.EnumerateFiles(modDir, "*.paa", SearchOption.AllDirectories))
        {
            var rel = PathResolver.RelativeTo(file, modDir);
            if (rel is null || PathResolver.IsExcluded(rel, excludes)) continue;
            var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (!TextureRoleSuffixes.Any(s => stem.EndsWith(s)))
                report.Info("texture-suffix",
                    $"Texture has no role suffix (_co/_nohq/_smdi): {rel} — fine for UI images, wrong for model textures.",
                    rel);
        }
    }

    private static bool IsConcatenated(string content, Match m)
    {
        static char PrevNonSpace(string s, int i)
        {
            for (int p = i - 1; p >= 0; p--)
                if (!char.IsWhiteSpace(s[p])) return s[p];
            return '\0';
        }
        static char NextNonSpace(string s, int i)
        {
            for (int p = i; p < s.Length; p++)
                if (!char.IsWhiteSpace(s[p])) return s[p];
            return '\0';
        }
        return PrevNonSpace(content, m.Index) == '+' || NextNonSpace(content, m.Index + m.Length) == '+';
    }

    private static bool LinePrefixIs(string content, int index, string token)
    {
        int lineStart = content.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var prefix = content[lineStart..index].TrimStart();
        return prefix.StartsWith(token, StringComparison.OrdinalIgnoreCase);
    }
}
