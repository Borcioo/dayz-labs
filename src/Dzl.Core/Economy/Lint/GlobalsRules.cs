using System.Globalization;

namespace Dzl.Core.Economy.Lint;

/// <summary>Per-file globals.xml checks: type is 0 (int) or 1 (float), and value parses as a number.
/// (The known-variable-name check is deferred — it needs the engine's canonical global list, and a
/// partial list would flag valid vars as unknown.)</summary>
public sealed class GlobalsRules : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: false, CeKind.Globals);

    public IEnumerable<LintFinding> Check(CeWorld world)
    {
        var file = world.FileNameOf(CeKind.Globals);
        foreach (var v in world.Globals)
        {
            if (v.Type is not (0 or 1))
                yield return new(LintSeverity.Warning, "global-type-range",
                    $"var '{v.Name}' type {v.Type} should be 0 (int) or 1 (float)",
                    file, v.Name, "type", CeKind.Globals, v.Name);

            if (!double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                yield return new(LintSeverity.Warning, "global-value-nan",
                    $"var '{v.Name}' value '{v.Value}' is not numeric",
                    file, v.Name, "value", CeKind.Globals, v.Name);
        }
    }
}
