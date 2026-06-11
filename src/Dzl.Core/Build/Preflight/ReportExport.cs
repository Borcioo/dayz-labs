using System.Text;
using System.Text.Json;

namespace Dzl.Core.Build.Preflight;

/// <summary>Serializes a <see cref="PreflightReport"/> for humans (.txt) and tools (.json).</summary>
public static class ReportExport
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string ToText(PreflightReport report, string modName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"dzl preflight — {modName}");
        sb.AppendLine($"errors: {report.Errors}  warnings: {report.Warnings}  info: {report.Infos}");
        sb.AppendLine($"checked: {report.CheckedFiles} files, {report.CheckedReferences} references, {report.CheckedConfigs} configs");
        sb.AppendLine();
        foreach (var f in report.Findings.OrderByDescending(f => f.Severity))
        {
            var loc = f.File.Length > 0 ? (f.Line > 0 ? $" [{f.File}:{f.Line}]" : $" [{f.File}]") : "";
            sb.AppendLine($"{f.Severity.ToString().ToUpperInvariant(),-7} {f.Rule}: {f.Message}{loc}");
        }
        if (report.Findings.Count == 0) sb.AppendLine("No findings.");
        return sb.ToString();
    }

    public static string ToJson(PreflightReport report, string modName) =>
        JsonSerializer.Serialize(new
        {
            mod = modName,
            ok = report.Ok,
            errors = report.Errors,
            warnings = report.Warnings,
            info = report.Infos,
            checked_files = report.CheckedFiles,
            checked_references = report.CheckedReferences,
            checked_configs = report.CheckedConfigs,
            findings = report.Findings,
        }, Json);

    /// <summary>Write both formats as <c>&lt;basePath&gt;.txt</c> / <c>.json</c>. Returns the two
    /// paths; failures are swallowed (reporting must never break the pipeline).</summary>
    public static (string txt, string json) Save(PreflightReport report, string modName, string basePath)
    {
        var txt = basePath + ".txt";
        var json = basePath + ".json";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            File.WriteAllText(txt, ToText(report, modName));
            File.WriteAllText(json, ToJson(report, modName));
        }
        catch { return ("", ""); }
        return (txt, json);
    }
}
