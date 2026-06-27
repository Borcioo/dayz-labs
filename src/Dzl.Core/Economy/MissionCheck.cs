using System.Text.RegularExpressions;
using Dzl.Core.Config;

namespace Dzl.Core.Economy;

public enum MissionSourceStatus { Instance, Install, Missing, Unknown }

/// <summary>Which mpmissions folder the <b>server</b> will actually load, derived from the instance's
/// serverDZ.cfg <c>template</c>. DayZ forces <c>$currentdir</c> to the exe dir, so a bare template name
/// resolves under the install — never the instance. This makes that visible.</summary>
/// <param name="Fixable">True when the instance has its own mission folder the template could be repointed
/// at (i.e. the dashboard "Fix" would change something). False when already correct, or when there's no
/// instance mission to point at.</param>
public sealed record MissionCheckResult(MissionSourceStatus Status, string EffectivePath, string Message, bool Fixable);

public static partial class MissionCheck
{
    [GeneratedRegex("template\\s*=\\s*\"([^\"]*)\"")] private static partial Regex TemplateRx();

    public static MissionCheckResult Evaluate(DzlConfig cfg)
    {
        var cfgPath = cfg.ConfigName;
        if (string.IsNullOrWhiteSpace(cfgPath) || !Path.IsPathRooted(cfgPath) || !File.Exists(cfgPath))
            return new(MissionSourceStatus.Unknown, "", "no instance serverDZ.cfg to read", false);

        string template;
        try { template = TemplateRx().Match(File.ReadAllText(cfgPath)).Groups[1].Value; }
        catch { return new(MissionSourceStatus.Unknown, "", "could not read serverDZ.cfg", false); }
        if (string.IsNullOrWhiteSpace(template))
            return new(MissionSourceStatus.Unknown, "", "no mission template in serverDZ.cfg", false);

        // The engine resolves a bare/relative template against $currentdir (the install dir), never the
        // instance — so model exactly that.
        var effective = Path.IsPathRooted(template)
            ? Path.GetFullPath(template)
            : Path.GetFullPath(Path.Combine(cfg.DayzPath, "mpmissions", template));

        var instance = MissionLocator.Resolve(cfg)?.MissionDir;
        // Fixable = there's an instance mission to point at, and the template isn't already on it.
        var fixable = instance is not null && !SamePath(effective, instance);

        if (instance is not null && SamePath(effective, instance))
            return new(MissionSourceStatus.Instance, effective, "server loads this instance's mission", false);
        if (!Directory.Exists(effective))
            return new(MissionSourceStatus.Missing, effective, "mission folder from template does not exist", fixable);
        if (IsUnder(effective, cfg.DayzPath))
            return new(MissionSourceStatus.Install, effective, "server loads the install's mission, not this instance's", fixable);
        return new(MissionSourceStatus.Unknown, effective, "template points outside the instance and the install", fixable);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root) =>
        Trim(path).StartsWith(Trim(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Trim(string p) =>
        Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
