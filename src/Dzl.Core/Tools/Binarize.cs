using Dzl.Core.Procs;

namespace Dzl.Core.Tools;

/// <summary>Thin wrapper around DayZ Tools' <c>binarize.exe</c> (folder mode). It converts the binarizable
/// models (MLOD <c>.p3d</c>, etc.) under <c>source</c> into <c>dest</c>, mirroring sub-paths; it does NOT
/// copy configs or other files (the build engine stages those itself). Never throws.</summary>
/// <remarks>
/// The flags mirror a known-good invocation (RaG-PBO-Builder's, studied behaviourally): <c>-binpath</c> +
/// <c>-addon</c> point binarize at the mounted project drive (<c>P:\</c>) so it can resolve vanilla and the
/// mod's own materials/configs — without them binarize spews "Material not loaded …rvmat" and is far slower.
/// <c>-always</c> rebuilds unconditionally, <c>-silent</c> suppresses prompts, <c>-maxProcesses</c> parallelises,
/// <c>-textures</c> gives it a scratch dir. With this context the SOURCE may live OFF the project drive (a plain
/// temp), so the engine no longer has to stage onto P:. binarize.exe is run with its working directory set to the
/// project drive root.
/// </remarks>
public static class Binarize
{
    public static List<string> Args(string sourceDir, string destDir, string binPath,
        IEnumerable<string> addonFolders, string texturesDir, int maxProcesses)
    {
        var bp = binPath.TrimEnd('\\', '/');
        if (bp.Length == 2 && bp[1] == ':') bp += "\\";   // a bare drive ("P:") must be "P:\"
        var a = new List<string>
        {
            "-targetBonesInterval=56",
            $"-maxProcesses={(maxProcesses < 1 ? 1 : maxProcesses)}",
            "-always",
            "-silent",
        };
        foreach (var f in addonFolders)
        {
            var folder = f.TrimEnd('\\', '/');
            if (folder.Length == 2 && folder[1] == ':') folder += "\\";
            a.Add($"-addon={folder}");
        }
        a.Add($"-textures={texturesDir}");
        a.Add($"-binpath={bp}");
        a.Add(sourceDir);   // SRC and DST must be the LAST two positional args
        a.Add(destDir);
        return a;
    }

    /// <param name="binPath">The mounted project drive root (e.g. <c>P:\</c>) used for <c>-binpath</c>/<c>-addon</c>
    /// and as the working directory, so binarize resolves references against the work drive.</param>
    public static (bool ok, string output) Run(string exePath, string sourceDir, string destDir, string binPath,
        IEnumerable<string> addonFolders, string texturesDir, int maxProcesses, Action<string>? onLine = null)
    {
        var workDir = binPath.TrimEnd('\\', '/');
        if (workDir.Length == 2 && workDir[1] == ':') workDir += "\\";
        var r = ProcRunner.Run(exePath, Args(sourceDir, destDir, binPath, addonFolders, texturesDir, maxProcesses),
            new RunOpts(WorkingDir: workDir, TimeoutMs: 0, OnLine: onLine));
        return (r.Ok, r.AllOutput);
    }
}
