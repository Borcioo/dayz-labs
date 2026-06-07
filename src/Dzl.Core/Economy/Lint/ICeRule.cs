namespace Dzl.Core.Economy.Lint;

public interface ICeRule { IEnumerable<LintFinding> Check(CeFileSet set, LimitsDef limits); }
