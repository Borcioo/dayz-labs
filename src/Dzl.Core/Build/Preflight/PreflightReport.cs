namespace Dzl.Core.Build.Preflight;

public enum FindingSeverity { Info, Warning, Error }

/// <summary>One preflight finding. <see cref="Rule"/> is a stable machine id (e.g. "cfgpatches-missing")
/// so frontends can group/filter; <see cref="File"/> is relative to the mod dir when possible;
/// <see cref="Line"/> is 1-based, 0 = whole-file/whole-mod finding.</summary>
public sealed record Finding(FindingSeverity Severity, string Rule, string Message, string File = "", int Line = 0);

/// <summary>Mutable collector the rules append to while the engine walks a mod project.
/// Counters are derived; <see cref="Ok"/> means "no errors" (warnings allowed).</summary>
public sealed class PreflightReport
{
    private readonly List<Finding> _findings = new();

    public IReadOnlyList<Finding> Findings => _findings;
    public int Errors => _findings.Count(f => f.Severity == FindingSeverity.Error);
    public int Warnings => _findings.Count(f => f.Severity == FindingSeverity.Warning);
    public int Infos => _findings.Count(f => f.Severity == FindingSeverity.Info);
    public bool Ok => Errors == 0;

    public int CheckedFiles { get; set; }
    public int CheckedReferences { get; set; }
    public int CheckedConfigs { get; set; }

    public void Error(string rule, string message, string file = "", int line = 0) =>
        _findings.Add(new Finding(FindingSeverity.Error, rule, message, file, line));
    public void Warn(string rule, string message, string file = "", int line = 0) =>
        _findings.Add(new Finding(FindingSeverity.Warning, rule, message, file, line));
    public void Info(string rule, string message, string file = "", int line = 0) =>
        _findings.Add(new Finding(FindingSeverity.Info, rule, message, file, line));
}
