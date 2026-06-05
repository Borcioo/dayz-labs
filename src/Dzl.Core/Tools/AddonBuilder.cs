using System.Diagnostics;
using System.Text;

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

    /// <summary>Runs AddonBuilder, capturing its log. When <paramref name="onLine"/> is supplied each
    /// output line is also streamed live (used by the tray build log). Never throws — a launch failure
    /// comes back as <c>Ok=false, ExitCode=-1</c> with the exception text as output.</summary>
    public static PackResult Pack(string exePath, string sourceDir, string outputDir,
        bool clear = true, bool packOnly = true, string? prefix = null, string? signKey = null,
        Action<string>? onLine = null)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath) { RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in PackArgs(sourceDir, outputDir, clear, packOnly, prefix, signKey)) psi.ArgumentList.Add(arg);

            var sb = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            void Sink(string? s)
            {
                if (s is null) return;
                lock (sb) sb.AppendLine(s);
                onLine?.Invoke(s);
            }
            p.OutputDataReceived += (_, e) => Sink(e.Data);
            p.ErrorDataReceived += (_, e) => Sink(e.Data);
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return new PackResult(p.ExitCode == 0, p.ExitCode, sb.ToString().Trim());
        }
        catch (Exception ex)
        {
            return new PackResult(false, -1, ex.Message);
        }
    }
}
