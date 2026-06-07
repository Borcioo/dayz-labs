namespace Dzl.Core.Economy.Lint;

public sealed class TypesRules : ICeRule
{
    public IEnumerable<LintFinding> Check(CeFileSet set, LimitsDef limits)
    {
        var findings = new List<LintFinding>();

        foreach (var e in set.Entries)
        {
            if (string.IsNullOrWhiteSpace(e.Name))
                findings.Add(new(LintSeverity.Error, "empty-name", "Type has no name", e.SourceFile, e.Name));

            if (limits.Category.Count > 0 && e.Category.Length > 0 && !limits.Category.Contains(e.Category))
                findings.Add(new(LintSeverity.Warning, "unknown-category", $"Category '{e.Category}' not in cfglimitsdefinition", e.SourceFile, e.Name));

            Unknown(e.Usage, limits.Usage, "unknown-usage", "Usage", e, findings);
            Unknown(e.Value, limits.Value, "unknown-value", "Value", e, findings);
            Unknown(e.Tag,   limits.Tag,   "unknown-tag",   "Tag",   e, findings);

            // NOTE: nominal=0 with min>0 is NOT flagged — it's valid + extremely common in real DayZ CE
            // (attachments/ammo/variants spawn via cargo/spawnabletypes, not the nominal distribution).
            // A rule for it floods lint with ~1000 false positives on vanilla (verified live 2026-06-07).
            if (e.Nominal > 0 && e.Min > e.Nominal)
                findings.Add(new(LintSeverity.Warning, "min-gt-nominal", $"min ({e.Min}) > nominal ({e.Nominal})", e.SourceFile, e.Name));
            if (e.QuantMin >= 0 && e.QuantMax >= 0 && e.QuantMin > e.QuantMax)
                findings.Add(new(LintSeverity.Warning, "quant-min-gt-max", $"quantmin ({e.QuantMin}) > quantmax ({e.QuantMax})", e.SourceFile, e.Name));
        }

        foreach (var dup in set.Entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                                       .Where(g => g.Key.Length > 0 && g.Count() > 1))
            findings.Add(new(LintSeverity.Error, "duplicate-name", $"'{dup.Key}' defined in {dup.Count()} places", dup.First().SourceFile, dup.Key));

        return findings;
    }

    private static void Unknown(IReadOnlyList<string> got, IReadOnlySet<string> valid, string code, string label,
                                TypeEntry e, List<LintFinding> findings)
    {
        if (valid.Count == 0) return;   // no definition loaded → don't flag
        foreach (var v in got.Where(v => v.Length > 0 && !valid.Contains(v)))
            findings.Add(new(LintSeverity.Warning, code, $"{label} '{v}' not in cfglimitsdefinition", e.SourceFile, e.Name));
    }
}
