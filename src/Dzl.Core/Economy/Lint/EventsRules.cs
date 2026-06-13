namespace Dzl.Core.Economy.Lint;

/// <summary>events.xml checks: min/max ordering, and the cross-file rule that every child <c>type</c>
/// exists in types.xml. The "children weights sum to 100" hint is Info-level (the %-weight convention
/// holds for the common ambient/dynamic spawners but not universally, so it must not flood as a warning).</summary>
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

            var sum = ev.Children.Sum(c => c.Min);
            if (ev.Children.Count > 1 && sum != 0 && sum != 100)
                yield return new(LintSeverity.Info, "event-children-weight-sum",
                    $"children min weights sum to {sum} (the spawn-weight convention is 100)",
                    file, ev.Name, "children", CeKind.Events, ev.Name);
        }
    }
}
