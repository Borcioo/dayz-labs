using System.Text.RegularExpressions;

namespace Dzl.Core.Build.Preflight.Rules;

/// <summary>Cheap textual lint over Enforce Script (<c>.c</c>) plus known config-trap patterns.</summary>
/// <remarks>Everything here fails *silently* in game — no RPT error, the feature just doesn't work —
/// which is exactly why a build-time warning earns its keep.</remarks>
public static class ScriptRules
{
    private static readonly Regex ModdedWithBaseRegex = new(
        @"\bmodded\s+class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:extends\s+([A-Za-z_][A-Za-z0-9_]*)|:\s*([A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClassDeclRegex = new(
        @"\b(?<modded>modded\s+)?class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b[^;{]*\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SetActionsRegex = new(
        @"\boverride\s+void\s+SetActions\s*\(\s*\)\s*\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SuperSetActionsRegex = new(
        @"\bsuper\s*\.\s*SetActions\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Check(string modDir, PreflightOptions opts, PreflightReport report)
    {
        var excludes = PathResolver.EffectiveExcludes(opts);
        var classDefs = new List<(string Name, string Rel, int Line)>();

        foreach (var file in Directory.EnumerateFiles(modDir, "*.c", SearchOption.AllDirectories))
        {
            var rel = PathResolver.RelativeTo(file, modDir);
            if (rel is null || PathResolver.IsExcluded(rel, excludes)) continue;

            string raw;
            try { raw = File.ReadAllText(file); } catch { continue; }
            var content = CppText.StripComments(raw, preserveLines: true);
            report.CheckedFiles++;

            CheckModdedBaseClause(content, rel, report);
            CheckSetActionsSuper(content, rel, report);
            CheckBalance(content, rel, report);
            CollectClassDefs(content, rel, classDefs);
        }

        ReportDuplicateClasses(classDefs, report);
        CheckConfigTraps(modDir, opts, excludes, report);
    }

    private static void CheckModdedBaseClause(string content, string rel, PreflightReport report)
    {
        foreach (Match m in ModdedWithBaseRegex.Matches(content))
        {
            var name = m.Groups[1].Value;
            report.Warn("modded-base-clause",
                $"modded class {name} declares a base class — the clause is a silent no-op in Enforce; write 'modded class {name}' alone.",
                rel, CppText.LineOf(content, m.Index));
        }
    }

    private static void CheckSetActionsSuper(string content, string rel, PreflightReport report)
    {
        foreach (Match m in SetActionsRegex.Matches(content))
        {
            int open = content.IndexOf('{', m.Index);
            int close = CppText.FindMatchingBrace(content, open);
            if (open < 0 || close < 0) continue;
            var body = content.Substring(open + 1, close - open - 1);
            if (!SuperSetActionsRegex.IsMatch(body))
                report.Warn("setactions-no-super",
                    "SetActions() override never calls super.SetActions() — inherited actions vanish from the item.",
                    rel, CppText.LineOf(content, m.Index));
        }
    }

    /// <summary>String/comment-aware bracket balance — catches truncated or mis-merged files.</summary>
    private static void CheckBalance(string content, string rel, PreflightReport report)
    {
        var stack = new Stack<(char C, int Line)>();
        var closers = new Dictionary<char, char> { ['}'] = '{', [')'] = '(', [']'] = '[' };
        char inString = '\0';
        bool escaped = false;
        int line = 1, reported = 0;

        foreach (var c in content)
        {
            if (c == '\n') line++;
            if (inString != '\0')
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == inString) inString = '\0';
                continue;
            }
            if (c is '"' or '\'') { inString = c; continue; }
            if (c is '{' or '(' or '[') stack.Push((c, line));
            else if (closers.TryGetValue(c, out var open))
            {
                if (stack.Count == 0 || stack.Peek().C != open)
                {
                    if (reported++ < 3)
                        report.Warn("script-balance", $"Unexpected '{c}' — brackets don't balance here.", rel, line);
                    if (stack.Count > 0) stack.Pop();
                }
                else stack.Pop();
            }
        }
        foreach (var (c, l) in stack.Take(3))
            report.Warn("script-balance", $"Unclosed '{c}' opened here.", rel, l);
    }

    private static void CollectClassDefs(string content, string rel, List<(string, string, int)> defs)
    {
        foreach (Match m in ClassDeclRegex.Matches(content))
        {
            if (m.Groups["modded"].Success) continue;
            defs.Add((m.Groups["name"].Value, rel, CppText.LineOf(content, m.Index)));
        }
    }

    private static void ReportDuplicateClasses(List<(string Name, string Rel, int Line)> defs,
        PreflightReport report)
    {
        foreach (var group in defs.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var places = string.Join(", ", group.Select(d => $"{d.Rel}:{d.Line}"));
            report.Warn("script-duplicate-class",
                $"class {group.First().Name} is defined {group.Count()} times ({places}) — extending an existing class needs 'modded class'.",
                group.First().Rel, group.First().Line);
        }
    }

    /// <summary>Config patterns that parse fine and then do nothing in game.</summary>
    private static void CheckConfigTraps(string modDir, PreflightOptions opts, string[] excludes,
        PreflightReport report)
    {
        foreach (var cfg in ConfigRules.DiscoverConfigs(modDir, excludes))
        {
            var rel = PathResolver.RelativeTo(cfg, modDir) ?? cfg;
            string content;
            try { content = CppText.StripComments(File.ReadAllText(cfg), preserveLines: true); }
            catch { continue; }

            foreach (Match m in Regex.Matches(content, @"\binventorySlot\s*\[\s*\]\s*\+=",
                RegexOptions.IgnoreCase))
                report.Warn("trap-inventoryslot-append",
                    "inventorySlot[] += onto a vanilla item whose inventorySlot is a STRING is silently dropped (Bohemia T148506, won't-fix). Redeclare the full array including the original slot.",
                    rel, CppText.LineOf(content, m.Index));

            foreach (Match m in Regex.Matches(content, @"\bhealthLevelValues\s*\[\s*\]",
                RegexOptions.IgnoreCase))
                report.Warn("trap-healthlevelvalues",
                    "healthLevelValues[] is the legacy format — it parses and does nothing. Use healthLevels[] nested under DamageSystem > GlobalHealth > Health.",
                    rel, CppText.LineOf(content, m.Index));
        }
    }
}
