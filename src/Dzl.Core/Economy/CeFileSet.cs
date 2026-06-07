namespace Dzl.Core.Economy;

/// <summary>The loaded multi-file Types view: entries across all resolved files, each tagged with its
/// <see cref="TypeEntry.SourceFile"/>. Pure container + grouping helpers.</summary>
public sealed class CeFileSet
{
    public IReadOnlyList<TypeEntry> Entries { get; }
    public CeFileSet(IEnumerable<TypeEntry> entries) => Entries = entries.ToList();

    public Dictionary<string, List<TypeEntry>> BySourceFile() =>
        Entries.GroupBy(e => e.SourceFile, StringComparer.OrdinalIgnoreCase)
               .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    public List<string> DistinctSources() =>
        Entries.Select(e => e.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase)
               .Where(s => s.Length > 0).ToList();
}
