using Dzl.Core.Procs;

namespace Dzl.Core.Tools;

public static class CfgConvert
{
    public static List<string> UnbinarizeArgs(string binPath, string outCpp) =>
        new() { "-txt", "-dst", outCpp, binPath };

    public static (bool ok, string output) Unbinarize(string exePath, string binPath, string outCpp) =>
        Run(exePath, UnbinarizeArgs(binPath, outCpp), Path.GetDirectoryName(binPath));

    public static List<string> ToBinArgs(string cppPath, string outBin) =>
        new() { "-bin", "-dst", outBin, cppPath };

    /// <summary>Convert a <c>config.cpp</c> to binary form — used both for real conversion and as a
    /// syntax gate (CfgConvert is the engine's own parser, so a non-zero exit is an authoritative
    /// config error). Runs with cwd at the config's folder so relative includes resolve.</summary>
    public static (bool ok, string output) ToBin(string exePath, string cppPath, string outBin) =>
        Run(exePath, ToBinArgs(cppPath, outBin), Path.GetDirectoryName(cppPath));

    private static (bool ok, string output) Run(string exePath, List<string> args, string? cwd)
    {
        var r = ProcRunner.Run(exePath, args, new RunOpts(WorkingDir: cwd));
        return (r.Ok, r.AllOutput);
    }
}
