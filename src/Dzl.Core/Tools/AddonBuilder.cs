using System.Diagnostics;
using System.Text;

namespace Dzl.Core.Tools;

public sealed record PackResult(bool Ok, int ExitCode, string Output);

public static class AddonBuilder
{
    public static List<string> PackArgs(string sourceDir, string outputDir, bool clear, bool packOnly,
                                        string? prefix, string? signKey,
                                        string? tempDir = null, string? includeFile = null)
    {
        var a = new List<string> { sourceDir, outputDir };
        if (clear) a.Add("-clear");
        if (packOnly) a.Add("-packonly");
        if (!string.IsNullOrWhiteSpace(prefix)) a.Add($"-prefix={prefix}");
        if (!string.IsNullOrWhiteSpace(signKey)) a.Add($"-sign={signKey}");
        // Per-mod temp keeps AddonBuilder state from leaking between builds (and survives for
        // debugging on failure); the include file adds extensions AddonBuilder silently drops
        // by default (officially documented for *.xml / *.nm in the terrain tutorial).
        if (!string.IsNullOrWhiteSpace(tempDir)) a.Add($"-temp={tempDir}");
        if (!string.IsNullOrWhiteSpace(includeFile)) a.Add($"-include={includeFile}");
        return a;
    }

    /// <summary>Copy-direct patterns for the <c>-include=</c> list: file types the engine reads
    /// at runtime but AddonBuilder won't pack unless told to.</summary>
    public static readonly string[] DefaultIncludePatterns =
    {
        "*.xml", "*.json", "*.csv", "*.layout", "*.imageset", "*.edds",
        "*.ogg", "*.wav", "*.nm", "*.bisurf", "*.html", "*.txt",
    };

    /// <summary>Write the default include-patterns file (one pattern per line) and return its path.</summary>
    public static string WriteIncludeFile(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "include.lst");
        File.WriteAllLines(path, DefaultIncludePatterns);
        return path;
    }

    /// <summary>Runs AddonBuilder, capturing its log. When <paramref name="onLine"/> is supplied each
    /// output line is also streamed live (used by the tray build log). Never throws — a launch failure
    /// comes back as <c>Ok=false, ExitCode=-1</c> with the exception text as output.</summary>
    public static PackResult Pack(string exePath, string sourceDir, string outputDir,
        bool clear = true, bool packOnly = true, string? prefix = null, string? signKey = null,
        Action<string>? onLine = null, string? tempDir = null, string? includeFile = null)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath) { RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in PackArgs(sourceDir, outputDir, clear, packOnly, prefix, signKey, tempDir, includeFile)) psi.ArgumentList.Add(arg);

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
