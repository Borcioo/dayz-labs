namespace Dzl.Core.Economy.Lint;

public enum LintSeverity { Error, Warning, Info }
public sealed record LintFinding(LintSeverity Severity, string Code, string Message, string File, string EntryName);
