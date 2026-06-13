namespace Dzl.Core.Economy.Lint;

/// <summary>A rule's scope: cross-file (heavy — only run in the dashboard's full pass) vs per-file
/// (light — also run live in that file's editor). <c>Kind</c> is the file the rule primarily reports
/// on, used for dashboard grouping and to pick which editor runs it live.</summary>
public readonly record struct RuleScope(bool CrossFile, CeKind Kind);

/// <summary>A validation rule over the whole loaded CE world. Pure — no I/O, never throws.</summary>
public interface ICeWorldRule
{
    RuleScope Scope { get; }
    IEnumerable<LintFinding> Check(CeWorld world);
}
