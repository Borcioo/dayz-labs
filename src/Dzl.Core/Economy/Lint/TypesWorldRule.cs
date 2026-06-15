namespace Dzl.Core.Economy.Lint;

/// <summary>Per-file Types validation over the CE world — delegates to the existing
/// <see cref="TypesRules"/> (ranges, duplicate names, usage/value/tag/category vs the dictionaries),
/// so the dashboard and the Types editor share one rule set. Findings carry the default
/// <see cref="CeKind.Types"/>.</summary>
public sealed class TypesWorldRule : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: false, CeKind.Types);

    public IEnumerable<LintFinding> Check(CeWorld world) =>
        new TypesRules().Check(world.Types, world.Limits.WithCombos(world.UserGroups));
}
