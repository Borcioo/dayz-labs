using Dzl.Core.Procs;

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

    // Manual-verify: runs the real exe per job, reports per-file result + progress. Never throws —
    // a launch failure surfaces as a failed PaaResult for that job.
    public static List<PaaResult> ConvertFolder(string exePath, string dir, bool recursive,
                                                IProgress<PaaResult>? progress = null)
    {
        var results = new List<PaaResult>();
        foreach (var job in PlanFolder(dir, recursive))
        {
            var run = ProcRunner.Run(exePath, new[] { job.Input, job.Output },
                new RunOpts(TimeoutMs: 120_000));
            var r = new PaaResult(job.Input, job.Output, run.Ok && File.Exists(job.Output),
                run.Ok ? "ok" : $"exit {run.Code}: {run.StdErr}");
            results.Add(r); progress?.Report(r);
        }
        return results;
    }
}
