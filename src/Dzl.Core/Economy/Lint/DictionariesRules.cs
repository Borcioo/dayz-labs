namespace Dzl.Core.Economy.Lint;

/// <summary>Cross-file: every member of a cfglimitsdefinitionuser.xml named list must exist in the
/// base cfglimitsdefinition.xml set of the matching kind (usage→usageflags, value→valueflags).
/// Skipped when the base set isn't loaded, to avoid flagging everything when the file is absent.</summary>
public sealed class DictionariesRules : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: true, CeKind.Dictionaries);

    public IEnumerable<LintFinding> Check(CeWorld world)
    {
        const string file = "cfglimitsdefinitionuser.xml";
        foreach (var g in world.UserGroups)
        {
            var baseSet = BaseSet(world.Limits, g.Kind);
            if (baseSet.Count == 0) continue;   // base dictionary not loaded → don't flag

            foreach (var m in g.Members.Where(m => m.Length > 0 && !baseSet.Contains(m)))
                yield return new(LintSeverity.Error, "user-list-unknown-member",
                    $"user list '{g.Name}' references '{m}', which is not defined in cfglimitsdefinition",
                    file, g.Name, "member", CeKind.Dictionaries, g.Name);
        }
    }

    private static IReadOnlySet<string> BaseSet(LimitsDef limits, LimitsKind kind) => kind switch
    {
        LimitsKind.Usage => limits.Usage,
        LimitsKind.Value => limits.Value,
        LimitsKind.Tag => limits.Tag,
        LimitsKind.Category => limits.Category,
        _ => new HashSet<string>(),
    };
}
