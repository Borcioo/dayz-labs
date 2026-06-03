using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class CfgConvert
{
    public static List<string> UnbinarizeArgs(string binPath, string outCpp) =>
        new() { "-txt", "-dst", outCpp, binPath };

    public static (bool ok, string output) Unbinarize(string exePath, string binPath, string outCpp)
    {
        var psi = new ProcessStartInfo(exePath) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in UnbinarizeArgs(binPath, outCpp)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode == 0, outp.Trim());
    }
}
