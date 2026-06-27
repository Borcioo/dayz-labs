using Dzl.Core.Procs;

namespace Dzl.Core.Tools;

/// <summary>Thin wrapper around DayZ Tools' <c>binarize.exe</c>. It converts the binarizable assets
/// (models, etc.) under <c>source</c> into <c>dest</c>, mirroring sub-paths; it does NOT copy configs or
/// other files (the build engine stages those itself). Never throws.</summary>
public static class Binarize
{
    public static List<string> Args(string sourceDir, string destDir) => new() { sourceDir, destDir };

    public static (bool ok, string output) Run(string exePath, string sourceDir, string destDir,
        Action<string>? onLine = null)
    {
        var r = ProcRunner.Run(exePath, Args(sourceDir, destDir), new RunOpts(TimeoutMs: 0, OnLine: onLine));
        return (r.Ok, r.AllOutput);
    }
}
