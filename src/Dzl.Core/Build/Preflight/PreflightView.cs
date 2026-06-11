namespace Dzl.Core.Build.Preflight;

/// <summary>Outcome of a preflight run, as surfaced to frontends. <see cref="Ok"/> = no
/// error-severity findings. <see cref="ReportTxt"/>/<see cref="ReportJson"/> are saved next to
/// the mod's build dir (empty when saving failed or was skipped).</summary>
public sealed record PreflightView(
    bool Ok, string Mod, int Errors, int Warnings, int Infos,
    IReadOnlyList<Finding> Findings, string ReportTxt, string ReportJson);
