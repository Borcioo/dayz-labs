namespace Dzl.Core.Economy.Lint;

/// <summary>events.xml checks: min/max ordering, and the cross-file rule that every child <c>type</c>
/// exists in types.xml. The "children weights sum to 100" hint is Info-level and only considers WEIGHT
/// children (<c>max=0</c>, where <c>min</c> is a % distribution); count-range children (<c>max&gt;0</c> —
/// vehicles, herds) are excluded so the hint can't flood on legitimate literal-count events.</summary>
public sealed class EventsRules : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: true, CeKind.Events);

    public IEnumerable<LintFinding> Check(CeWorld world)
    {
        var file = world.FileNameOf(CeKind.Events);

        foreach (var ev in world.Events)
        {
            if (ev.Max > 0 && ev.Min > ev.Max)
                yield return new(LintSeverity.Warning, "event-min-gt-max",
                    $"min ({ev.Min}) > max ({ev.Max})", file, ev.Name, "min", CeKind.Events, ev.Name);

            foreach (var c in ev.Children)
                if (world.TypeNames.Count > 0 && c.Type.Length > 0 && !world.TypeNames.Contains(c.Type))
                    yield return new(LintSeverity.Warning, "event-child-not-in-types",
                        $"child '{c.Type}' has no entry in types.xml", file, ev.Name, "children", CeKind.Events, ev.Name);

            // A child's min is a percentage spawn-WEIGHT only when max=0; when max>0, min/max are a literal
            // count range (vehicles spawn 3-5, herds spawn N per type) whose sum is meaningless. Only the
            // weight kind follows the "sum to 100" convention, and only a multi-type set is worth flagging.
            var weights = ev.Children.Where(c => c.Max == 0).ToList();
            var sum = weights.Sum(c => c.Min);
            if (weights.Count > 1 && sum != 0 && sum != 100)
                yield return new(LintSeverity.Info, "event-children-weight-sum",
                    $"children spawn-weights sum to {sum} (the convention is 100 so they read as %)",
                    file, ev.Name, "children", CeKind.Events, ev.Name);
        }
    }
}
