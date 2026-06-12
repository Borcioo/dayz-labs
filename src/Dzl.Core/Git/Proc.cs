using Dzl.Core.Procs;

namespace Dzl.Core.Vcs;

/// <summary>Runs an external CLI (git / gh) through <see cref="ProcRunner"/>, capturing
/// stdout+stderr+exit code. Never throws — a launch failure (exe missing, etc.) comes back as exit
/// <c>-1</c> with the message as stderr. Owns the git-specific concerns: PATH resolution for bare
/// command names and the environment that forces git/gh to fail fast instead of prompting.</summary>
internal static class Proc
{
    /// <summary>Hard cap so a hung CLI (git waiting on credentials, a lock, an editor/pager) can't block the
    /// caller forever — the process is killed and a timeout error returned past this.</summary>
    private const int DefaultTimeoutMs = 60_000;

    /// <summary>Force git/gh to NEVER block on an interactive prompt — fail fast instead of hanging the server.</summary>
    private static readonly IReadOnlyDictionary<string, string> GitEnv = new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",    // no username/password prompt
        ["GIT_OPTIONAL_LOCKS"] = "0",     // don't wait on index.lock for read-only ops
        ["GCM_INTERACTIVE"] = "Never",    // Git Credential Manager: no GUI prompt
        ["GIT_PAGER"] = "cat",            // never open a pager
        ["GH_PROMPT_DISABLED"] = "1",     // gh: no interactive prompts
    };

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
        var r = ProcRunner.Run(Resolve(exe), args, new RunOpts(
            WorkingDir: Directory.Exists(workdir) ? workdir : Environment.CurrentDirectory,
            TimeoutMs: DefaultTimeoutMs,
            Env: GitEnv));
        return (r.Code, r.StdOut, r.StdErr);
    }
}
