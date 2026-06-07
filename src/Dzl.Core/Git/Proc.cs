using System.Diagnostics;
using System.Text;

namespace Dzl.Core.Vcs;

/// <summary>Runs an external CLI (git / gh), capturing stdout+stderr+exit code. Never throws —
/// a launch failure (exe missing, etc.) comes back as exit <c>-1</c> with the message as stderr.
/// Mirrors the project convention that process wrappers return data, not exceptions.</summary>
internal static class Proc
{
    /// <summary>Hard cap so a hung CLI (git waiting on credentials, a lock, an editor/pager) can't block the
    /// caller forever — the process is killed and a timeout error returned past this.</summary>
    private const int DefaultTimeoutMs = 60_000;

    /// <summary>Serialises the spawn (handle setup + <c>Start</c>) across threads. Without this, two concurrent
    /// <c>Start</c>s can leak each other's inheritable redirected pipe handles into the sibling child, so a
    /// reader never sees EOF and the call hangs. Only the launch is locked; the children run in parallel.</summary>
    private static readonly object SpawnLock = new();

    /// <summary>Resolve a bare command name (git/gh) to a full path so it works even when the host process
    /// inherited a reduced PATH (e.g. the MCP server launched without Git on PATH). Falls back to the common
    /// Windows install locations; returns the name unchanged if nothing matches (let it fail with its own error).</summary>
    private static string Resolve(string exe)
    {
        if (exe.Contains('\\') || exe.Contains('/')) return exe;   // already a path
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in new[] { ".exe", ".cmd", "" })
                try { var f = Path.Combine(dir.Trim(), exe + ext); if (File.Exists(f)) return f; } catch { /* bad PATH entry */ }
        }
        string[] fallbacks = exe.Equals("git", StringComparison.OrdinalIgnoreCase)
            ? new[] { @"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files (x86)\Git\cmd\git.exe" }
            : exe.Equals("gh", StringComparison.OrdinalIgnoreCase)
                ? new[] { @"C:\Program Files\GitHub CLI\gh.exe" }
                : Array.Empty<string>();
        foreach (var f in fallbacks) if (File.Exists(f)) return f;
        return exe;
    }

    public static (int code, string stdout, string stderr) Run(string exe, string workdir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Resolve(exe))
            {
                WorkingDirectory = Directory.Exists(workdir) ? workdir : Environment.CurrentDirectory,
                RedirectStandardInput = true,    // own the child's stdin so it can't inherit the host's
                RedirectStandardOutput = true,   // (a stdio MCP server's stdin is a live JSON-RPC pipe — an
                RedirectStandardError = true,    // inherited one makes git block forever instead of seeing EOF)
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            // Force git/gh to NEVER block on an interactive prompt — fail fast instead of hanging the server.
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";    // no username/password prompt
            psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";     // don't wait on index.lock for read-only ops
            psi.Environment["GCM_INTERACTIVE"] = "Never";    // Git Credential Manager: no GUI prompt
            psi.Environment["GIT_PAGER"] = "cat";            // never open a pager
            psi.Environment["GH_PROMPT_DISABLED"] = "1";     // gh: no interactive prompts

            var so = new StringBuilder();
            var se = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
            lock (SpawnLock)
            {
                p.Start();
                p.StandardInput.Close();   // signal EOF — these tools never feed git on stdin
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            if (!p.WaitForExit(DefaultTimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return (-1, so.ToString().Trim(), $"timed out after {DefaultTimeoutMs / 1000}s (process killed)");
            }
            p.WaitForExit();   // let the async output readers flush
            return (p.ExitCode, so.ToString().Trim(), se.ToString().Trim());
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
