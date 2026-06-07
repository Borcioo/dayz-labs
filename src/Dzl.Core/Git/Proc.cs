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

    public static (int code, string stdout, string stderr) Run(string exe, string workdir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Directory.Exists(workdir) ? workdir : Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

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
