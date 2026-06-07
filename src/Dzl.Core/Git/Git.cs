namespace Dzl.Core.Vcs;

/// <summary>Snapshot of a mod project's git state for the My-Mods cards / <c>dzl repo status</c>.
/// <see cref="IsRepo"/> false means the folder isn't a git work tree (the other fields are then defaults).</summary>
public sealed record RepoStatus(
    bool IsRepo, string Branch, int Ahead, int Behind, bool Dirty, bool HasRemote, string Detail);

/// <summary>Thin, never-throwing wrappers around the <c>git</c> CLI. The status parser is pure and
/// unit-tested; everything else shells out via <see cref="Proc"/>.</summary>
public static class Git
{
    public static bool IsAvailable() => Proc.Run("git", ".", "--version").code == 0;

    public static bool IsRepo(string dir) =>
        Directory.Exists(dir) && Proc.Run("git", dir, "rev-parse", "--is-inside-work-tree").code == 0;

    public static bool HasCommits(string dir) =>
        Proc.Run("git", dir, "rev-parse", "--verify", "HEAD").code == 0;

    /// <summary>Parse <c>git status --porcelain=v2 --branch</c> output into a <see cref="RepoStatus"/>.
    /// Pure — no I/O. Caller guarantees the dir is already a repo.</summary>
    public static RepoStatus ParseStatus(string porcelain)
    {
        string branch = "";
        int ahead = 0, behind = 0;
        bool hasRemote = false, dirty = false;

        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                branch = line["# branch.head ".Length..].Trim();
            else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
                hasRemote = true;
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var p in line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (p.Length > 1 && p[0] == '+' && int.TryParse(p[1..], out var a)) ahead = a;
                    else if (p.Length > 1 && p[0] == '-' && int.TryParse(p[1..], out var b)) behind = b;
                }
            }
            // Changed/renamed/unmerged/untracked entries (v2): lines starting "1 ", "2 ", "u ", "? ".
            else if (line.Length >= 2 && line[1] == ' ' && line[0] is '1' or '2' or 'u' or '?')
                dirty = true;
        }

        return new RepoStatus(true, branch, ahead, behind, dirty, hasRemote, dirty ? "dirty" : "clean");
    }

    public static RepoStatus Status(string dir)
    {
        if (!Directory.Exists(dir)) return new RepoStatus(false, "", 0, 0, false, false, "no such folder");
        var (code, outp, _) = Proc.Run("git", dir, "status", "--porcelain=v2", "--branch");
        return code == 0 ? ParseStatus(outp) : new RepoStatus(false, "", 0, 0, false, false, "not a git repo");
    }

    /// <summary>Browsable URL of the <c>origin</c> remote, or null when there's no remote. Shells out for the
    /// raw remote then normalises via <see cref="ToBrowserUrl"/>.</summary>
    public static string? RemoteUrl(string dir)
    {
        var (code, outp, _) = Proc.Run("git", dir, "remote", "get-url", "origin");
        return code == 0 ? ToBrowserUrl(outp.Trim()) : null;
    }

    /// <summary>Normalise a git remote (scp-like <c>git@host:owner/repo.git</c>, https, or ssh://) to a browsable
    /// https URL with any trailing <c>.git</c> stripped. Null when it isn't a recognisable remote. Pure.</summary>
    public static string? ToBrowserUrl(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        remote = remote.Trim();
        string url;
        if (remote.StartsWith("git@", StringComparison.Ordinal))            // git@github.com:owner/repo.git
        {
            var at = remote.IndexOf('@'); var colon = remote.IndexOf(':');
            if (colon <= at) return null;
            url = $"https://{remote[(at + 1)..colon]}/{remote[(colon + 1)..]}";
        }
        else if (remote.StartsWith("ssh://", StringComparison.Ordinal))     // ssh://git@host/owner/repo.git
            url = "https://" + remote["ssh://".Length..].Replace("git@", "");
        else if (remote.StartsWith("http://", StringComparison.Ordinal) || remote.StartsWith("https://", StringComparison.Ordinal))
            url = remote;
        else return null;
        return url.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? url[..^4] : url;
    }

    public static (bool ok, string msg) Init(string dir)
    {
        var (code, _, err) = Proc.Run("git", dir, "init", "-b", "main");
        return (code == 0, code == 0 ? "initialised" : err);
    }

    /// <summary>Stage everything and commit. Returns ok=false (with git's message) when there's nothing
    /// to commit or the commit fails.</summary>
    public static (bool ok, string msg) CommitAll(string dir, string message)
    {
        var add = Proc.Run("git", dir, "add", "-A");
        if (add.code != 0) return (false, add.stderr);
        var (code, outp, err) = Proc.Run("git", dir, "commit", "-m", message);
        return (code == 0, code == 0 ? outp : (err.Length > 0 ? err : outp));
    }
}
