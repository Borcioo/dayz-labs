namespace Dzl.Core.Vcs;

/// <summary>Thin, never-throwing wrappers around the <c>gh</c> CLI for the GitHub actions dzl exposes
/// (create + push a repo, cut a release). Auth is whatever <c>gh auth</c> already holds on the machine.</summary>
/// <summary>GitHub CLI auth state. <see cref="LoggedIn"/> false means no <c>gh</c> token (or gh missing).</summary>
public sealed record GhAuth(bool LoggedIn, string Account, string Detail);

public static class GitHub
{
    public static bool IsAvailable() => Proc.Run("gh", ".", "--version").code == 0;

    /// <summary>Report <c>gh</c> auth state by parsing <c>gh auth status</c> (exit 0 = logged in). No keys:
    /// auth is whatever the gh OAuth login already established on the machine.</summary>
    public static GhAuth AuthStatus()
    {
        if (!IsAvailable()) return new GhAuth(false, "", "gh (GitHub CLI) not installed");
        var (code, outp, err) = Proc.Run("gh", ".", "auth", "status");
        if (code != 0) return new GhAuth(false, "", "not logged in — click Login");

        var account = "";
        foreach (var line in (outp + "\n" + err).Split('\n'))
        {
            var i = line.IndexOf("account ", StringComparison.Ordinal);
            if (i >= 0) { account = line[(i + "account ".Length)..].Trim().Split(' ')[0]; break; }
        }
        return new GhAuth(true, account, account.Length > 0 ? $"logged in as {account}" : "logged in");
    }

    /// <summary>Log out of github.com via <c>gh auth logout</c>.</summary>
    public static (bool ok, string msg) Logout()
    {
        var (code, outp, err) = Proc.Run("gh", ".", "auth", "logout", "--hostname", "github.com");
        return (code == 0, Join(outp, err));
    }

    /// <summary>Clone a GitHub repo into <paramref name="destDir"/> (which must not yet exist) via
    /// <c>gh repo clone &lt;repo&gt; &lt;dir&gt;</c> — gh accepts an <c>owner/name</c> or a full URL and reuses
    /// its own auth. Returns (ok, message); never throws.</summary>
    public static (bool ok, string msg) Clone(string repo, string destDir)
    {
        var (code, outp, err) = Proc.Run("gh", ".", "repo", "clone", repo, destDir);
        return (code == 0, Join(outp, err));
    }

    /// <summary>Create a GitHub repo from an existing local repo and push it: wraps
    /// <c>gh repo create &lt;name&gt; --source=. --remote=origin --push</c>. The dir must already be a
    /// git repo with at least one commit.</summary>
    public static (bool ok, string msg) CreateRepo(string dir, string name, bool @private, string? description)
    {
        var args = new List<string>
        {
            "repo", "create", name,
            "--source=.", "--remote=origin", "--push",
            @private ? "--private" : "--public",
        };
        if (!string.IsNullOrWhiteSpace(description)) { args.Add("--description"); args.Add(description!); }

        var (code, outp, err) = Proc.Run("gh", dir, args.ToArray());
        return (code == 0, Join(outp, err));
    }

    /// <summary>Cut a GitHub release at HEAD (creates and pushes the tag): wraps
    /// <c>gh release create &lt;tag&gt;</c>. With no notes, GitHub auto-generates them.</summary>
    public static (bool ok, string msg) Release(string dir, string tag, string? title, string? notes)
    {
        var args = new List<string> { "release", "create", tag, "--title", string.IsNullOrWhiteSpace(title) ? tag : title! };
        if (string.IsNullOrWhiteSpace(notes)) args.Add("--generate-notes");
        else { args.Add("--notes"); args.Add(notes!); }

        var (code, outp, err) = Proc.Run("gh", dir, args.ToArray());
        return (code == 0, Join(outp, err));
    }

    private static string Join(string a, string b) =>
        string.Join("\n", new[] { a, b }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
}
