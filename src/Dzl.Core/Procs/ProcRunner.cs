using System.Diagnostics;
using System.Text;

namespace Dzl.Core.Procs;

/// <summary>Options for <see cref="ProcRunner.Run"/>. <see cref="TimeoutMs"/> &lt;= 0 disables the
/// cap (long builds); <see cref="OnLine"/> streams each output line live (build consoles);
/// <see cref="Env"/> adds/overrides environment variables for the child.</summary>
public sealed record RunOpts(
    string? WorkingDir = null,
    int TimeoutMs = 60_000,
    Action<string>? OnLine = null,
    IReadOnlyDictionary<string, string>? Env = null);

/// <summary>Outcome of a captured process run. A launch failure (exe missing, etc.) comes back as
/// <c>Code == -1</c> with the message in <see cref="StdErr"/> — never an exception.</summary>
public sealed record RunResult(int Code, string StdOut, string StdErr, bool TimedOut)
{
    public bool Ok => Code == 0 && !TimedOut;

    /// <summary>Stdout + stderr joined for callers that only show one blob.</summary>
    public string AllOutput =>
        StdOut.Length == 0 ? StdErr : StdErr.Length == 0 ? StdOut : StdOut + Environment.NewLine + StdErr;
}

/// <summary>
/// The one "run external process, capture output" implementation for Dzl.Core. Carries the
/// hardening every wrapper needs but only Git's runner historically had: stdin is redirected and
/// closed right after start (a stdio MCP server's stdin is a live JSON-RPC pipe — an inherited one
/// makes children block forever instead of seeing EOF), the spawn is serialised so concurrent
/// starts can't leak each other's inheritable pipe handles, output is drained by async readers
/// (sequential ReadToEnd deadlocks when the other pipe's buffer fills), and a timeout kills the
/// whole process tree. Never throws.
/// </summary>
public static class ProcRunner
{
    /// <summary>Serialises handle setup + <c>Start</c> across threads. Without this, two concurrent
    /// <c>Start</c>s can leak each other's inheritable redirected pipe handles into the sibling
    /// child, so a reader never sees EOF and the call hangs. Only the launch is locked; the
    /// children run in parallel.</summary>
    private static readonly object SpawnLock = new();

    public static RunResult Run(string exe, IReadOnlyList<string> args, RunOpts? opts = null)
    {
        var o = opts ?? new RunOpts();
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(o.WorkingDir) && Directory.Exists(o.WorkingDir))
                psi.WorkingDirectory = o.WorkingDir;
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (o.Env is not null)
                foreach (var (k, v) in o.Env) psi.Environment[k] = v;

            var so = new StringBuilder();
            var se = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) { so.AppendLine(e.Data); o.OnLine?.Invoke(e.Data); } };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) { se.AppendLine(e.Data); o.OnLine?.Invoke(e.Data); } };
            lock (SpawnLock)
            {
                p.Start();
                p.StandardInput.Close();   // signal EOF — captured children are never fed on stdin
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            if (o.TimeoutMs > 0 && !p.WaitForExit(o.TimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return new RunResult(-1, so.ToString().Trim(),
                    $"timed out after {o.TimeoutMs / 1000}s (process killed)", TimedOut: true);
            }
            p.WaitForExit();   // let the async output readers flush
            return new RunResult(p.ExitCode, so.ToString().Trim(), se.ToString().Trim(), TimedOut: false);
        }
        catch (Exception ex)
        {
            return new RunResult(-1, "", ex.Message, TimedOut: false);
        }
    }
}
