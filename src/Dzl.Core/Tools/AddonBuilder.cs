using System.Diagnostics;

namespace Dzl.Core.Tools;

public sealed record PackResult(bool Ok, int ExitCode, string Output);

public static class AddonBuilder
{
    public static List<string> PackArgs(string sourceDir, string outputDir, bool clear, bool packOnly,
                                        string? prefix, string? signKey)
    {
        var a = new List<string> { sourceDir, outputDir };
        if (clear) a.Add("-clear");
        if (packOnly) a.Add("-packonly");
        if (!string.IsNullOrWhiteSpace(prefix)) a.Add($"-prefix={prefix}");
        if (!string.IsNullOrWhiteSpace(signKey)) a.Add($"-sign={signKey}");
        return a;
    }

    public static PackResult Pack(string exePath, string sourceDir, string outputDir,
        bool clear = true, bool packOnly = true, string? prefix = null, string? signKey = null)
    {
        var psi = new ProcessStartInfo(exePath) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in PackArgs(sourceDir, outputDir, clear, packOnly, prefix, signKey)) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new PackResult(p.ExitCode == 0, p.ExitCode, outp.Trim());
    }
}
