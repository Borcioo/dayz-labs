namespace Dzl.Core.Vcs;

/// <summary>Thin, never-throwing wrappers around the <c>gh</c> CLI for the GitHub actions dzl exposes
/// (create + push a repo, cut a release). Auth is whatever <c>gh auth</c> already holds on the machine.</summary>
public static class GitHub
{
    public static bool IsAvailable() => Proc.Run("gh", ".", "--version").code == 0;

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
