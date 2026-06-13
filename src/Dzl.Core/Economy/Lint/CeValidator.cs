namespace Dzl.Core.Economy.Lint;

/// <summary>Runs CE validation rules over a loaded <see cref="CeWorld"/>. The full pass runs every
/// rule (with optional 0–100 progress); the per-file pass runs only the light rules for one file
/// kind (cheap enough to fire live in the editor). The default rule set registers all shipped rules.</summary>
public sealed class CeValidator
{
    private readonly IReadOnlyList<ICeWorldRule> _rules;

    public CeValidator(IEnumerable<ICeWorldRule>? rules = null) => _rules = (rules ?? DefaultRules()).ToList();

    /// <summary>Run every rule. Reports progress as a 0–100 percentage after each rule.</summary>
    public IReadOnlyList<LintFinding> ValidateFull(CeWorld world, IProgress<int>? progress = null)
    {
        var all = new List<LintFinding>();
        for (var i = 0; i < _rules.Count; i++)
        {
            all.AddRange(_rules[i].Check(world));
            progress?.Report((i + 1) * 100 / _rules.Count);
        }
        return all;
    }

    /// <summary>Run only the per-file (non-cross-file) rules for one file kind — for live editor checks.</summary>
    public IReadOnlyList<LintFinding> ValidatePerFile(CeWorld world, CeKind kind) =>
        _rules.Where(r => !r.Scope.CrossFile && r.Scope.Kind == kind)
              .SelectMany(r => r.Check(world))
              .ToList();

    public static IReadOnlyList<ICeWorldRule> DefaultRules() => new ICeWorldRule[]
    {
        new TypesWorldRule(),
        new SpawnableTypesRules(),
        new EventsRules(),
    };
}
