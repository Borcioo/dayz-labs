using System.Diagnostics;
using System.Text;

namespace Dzl.Core.Vcs;

/// <summary>Runs an external CLI (git / gh), capturing stdout+stderr+exit code. Never throws —
/// a launch failure (exe missing, etc.) comes back as exit <c>-1</c> with the message as stderr.
/// Mirrors the project convention that process wrappers return data, not exceptions.</summary>
internal static class Proc
{
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

            var so = new StringBuilder();
            var se = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return (p.ExitCode, so.ToString().Trim(), se.ToString().Trim());
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
