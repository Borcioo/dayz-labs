namespace Dzl.Core.Economy.Lint;

public enum LintSeverity { Error, Warning, Info }

/// <summary>One validation finding. <c>Field</c>/<c>Kind</c>/<c>Target</c> are optional so the
/// original five-arg callers keep compiling; the CE-world rules set them for richer dashboards and
/// click-to-jump (<c>Target</c> = the entry id the editor should select; <see cref="CeKind"/> from
/// <c>Dzl.Core.Economy</c> groups by file).</summary>
public sealed record LintFinding(
    LintSeverity Severity, string Code, string Message, string File, string EntryName,
    string Field = "", CeKind Kind = CeKind.Types, string Target = "");
