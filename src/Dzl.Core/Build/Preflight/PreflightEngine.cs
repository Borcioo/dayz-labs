using Dzl.Core.Build.Preflight.Rules;

namespace Dzl.Core.Build.Preflight;

/// <summary>
/// Walks one mod project dir and runs the enabled rule families. Never throws on bad input —
/// a missing dir is itself a finding. Frontends decide whether errors block a build.
/// </summary>
public static class PreflightEngine
{
    public static PreflightReport Run(string modDir, string modName, PreflightOptions? options = null)
    {
        var opts = options ?? new PreflightOptions();
        var report = new PreflightReport();

        if (!Directory.Exists(modDir))
        {
            report.Error("mod-missing", $"Mod directory does not exist: {modDir}");
            return report;
        }

        var prefix = PathResolver.ReadPrefix(modDir);
        if (prefix.Length == 0) prefix = modName;

        if (opts.CheckConfig)
        {
            ConfigRules.CheckPrefixFile(modDir, modName, report);
            ConfigRules.CheckConfigs(modDir, modName, prefix, opts, report);
        }

        if (opts.CheckReferences)
        {
            ReferenceRules.Check(modDir, prefix, opts, report);
            ReferenceRules.CheckTextureSuffixes(modDir, opts, report);
        }

        if (opts.CheckFileSystem)
            FileSystemRules.Check(modDir, prefix, opts, report);

        if (opts.CheckScripts)
            ScriptRules.Check(modDir, opts, report);

        return report;
    }
}
