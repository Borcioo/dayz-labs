using System.Diagnostics;

namespace Dzl.Core.Tools;

public sealed record PaaJob(string Input, string Output, bool SuffixOk);
public sealed record PaaResult(string Input, string Output, bool Ok, string Message);

public static class ImageToPaa
{
    private static readonly string[] Suffixes =
        { "_co", "_ca", "_nohq", "_smdi", "_as", "_dt", "_mc", "_nofhq", "_sky", "_detail" };

    public static bool HasValidSuffix(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return Suffixes.Any(s => stem.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    public static (string input, string output) ConvertArgs(string input) =>
        (input, Path.ChangeExtension(input, ".paa"));

    public static List<PaaJob> PlanFolder(string dir, bool recursive)
    {
        if (!Directory.Exists(dir)) return new();
        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(dir, "*.*", opt)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            .Select(f => { var (i, o) = ConvertArgs(f); return new PaaJob(i, o, HasValidSuffix(f)); })
            .ToList();
    }

    // Manual-verify: runs the real exe per job, reports per-file result + progress.
    public static List<PaaResult> ConvertFolder(string exePath, string dir, bool recursive,
                                                IProgress<PaaResult>? progress = null)
    {
        var results = new List<PaaResult>();
        foreach (var job in PlanFolder(dir, recursive))
        {
            var psi = new ProcessStartInfo(exePath) { RedirectStandardError = true, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(job.Input);
            psi.ArgumentList.Add(job.Output);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            var r = new PaaResult(job.Input, job.Output, p.ExitCode == 0 && File.Exists(job.Output),
                p.ExitCode == 0 ? "ok" : $"exit {p.ExitCode}: {err.Trim()}");
            results.Add(r); progress?.Report(r);
        }
        return results;
    }
}
