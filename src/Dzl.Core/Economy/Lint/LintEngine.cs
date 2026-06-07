namespace Dzl.Core.Economy.Lint;

/// <summary>Runs the registered CE rules over a file set. CE0 ships the Types rules; later sub-projects
/// register more rules here.</summary>
public sealed class LintEngine
{
    private readonly IReadOnlyList<ICeRule> _rules;
    public LintEngine(IEnumerable<ICeRule>? rules = null)
        => _rules = rules?.ToList() ?? new List<ICeRule> { new TypesRules() };

    public IReadOnlyList<LintFinding> Run(CeFileSet set, LimitsDef limits)
        => _rules.SelectMany(r => r.Check(set, limits)).ToList();
}
