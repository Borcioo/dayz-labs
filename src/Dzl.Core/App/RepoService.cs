using Dzl.Core.Config;
using Dzl.Core.Projects;
using Dzl.Core.Vcs;

namespace Dzl.Core.App;

/// <summary>
/// SP4 GitHub integration: treat each mod project as a git repo manageable from dzl — report status,
/// publish to GitHub (init + first commit + <c>gh repo create --push</c>), and cut releases. One facade
/// per frontend (CLI/MCP/tray); the git/gh shelling lives in <see cref="Git"/> / <see cref="GitHub"/>.
/// </summary>
public sealed class RepoService
{
    private readonly string _configPath;
    public RepoService(string configPath) { _configPath = configPath; }

    private const string GitIgnore =
        "# dzl / DayZ build artifacts\n*.pbo\n*.bin\n*.log\nlogs/\n\n# editor / OS noise\n.vs/\n*.tmp\nThumbs.db\n";

    /// <summary>Resolve a project dir under ProjectsRoot, or return null + an error message.</summary>
    private string? ResolveProject(string mod, out string error)
    {
        error = "";
        if (!ProjectPaths.IsValidName(mod)) { error = $"invalid mod name: {mod}"; return null; }
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        var dir = ProjectPaths.ModDir(ProjectPaths.Root(cfg), mod);
        if (!Directory.Exists(dir) || !ModProjects.IsProject(dir))
        {
            error = $"not a mod project: {dir} (need $PBOPREFIX$ or config.cpp)";
            return null;
        }
        return dir;
    }

    /// <summary>Git state of a mod project (drives the My-Mods cards + <c>dzl repo status</c>).</summary>
    public RepoStatus Status(string mod)
    {
        var dir = ResolveProject(mod, out var err);
        return dir is null ? new RepoStatus(false, "", 0, 0, false, false, err) : Git.Status(dir);
    }

    /// <summary>Init the repo if needed (with a DayZ <c>.gitignore</c> + first commit) then create and push
    /// a GitHub repo named after the mod.</summary>
    public OpResult Publish(string mod, bool @private = true, string? description = null)
    {
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return new OpResult(false, err);
        if (!Git.IsAvailable()) return new OpResult(false, "git not found on PATH");
        if (!GitHub.IsAvailable()) return new OpResult(false, "gh (GitHub CLI) not found — install + 'gh auth login'");

        if (!Git.IsRepo(dir))
        {
            var init = Git.Init(dir);
            if (!init.ok) return new OpResult(false, $"git init failed: {init.msg}");
        }

        // Seed a .gitignore (idempotent) so build artifacts don't get committed.
        var gi = Path.Combine(dir, ".gitignore");
        if (!File.Exists(gi)) { try { File.WriteAllText(gi, GitIgnore); } catch { /* best-effort */ } }

        // gh repo create --push needs at least one commit.
        if (!Git.HasCommits(dir))
        {
            var commit = Git.CommitAll(dir, "Initial commit (dzl)");
            if (!commit.ok) return new OpResult(false, $"first commit failed: {commit.msg}");
        }

        var (ok, msg) = GitHub.CreateRepo(dir, mod, @private, description);
        return new OpResult(ok, ok ? $"published {mod} → {msg}" : $"gh repo create failed: {msg}");
    }

    /// <summary>Cut a GitHub release at HEAD for the mod (creates + pushes the tag).</summary>
    public OpResult Release(string mod, string tag, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(tag)) return new OpResult(false, "tag required (e.g. v1.0.0)");
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return new OpResult(false, err);
        if (!GitHub.IsAvailable()) return new OpResult(false, "gh (GitHub CLI) not found");
        if (!Git.IsRepo(dir) || !Git.HasCommits(dir)) return new OpResult(false, "not a published repo yet — run publish first");

        var (ok, msg) = GitHub.Release(dir, tag, $"{mod} {tag}", notes);
        return new OpResult(ok, ok ? $"released {tag} → {msg}" : $"gh release failed: {msg}");
    }

    /// <summary>Changed files in a mod's repo (staged + unstaged + untracked).</summary>
    public (bool ok, string error, List<Git.ChangedFile> files) Changes(string mod)
    {
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return (false, err, new());
        if (!Git.IsRepo(dir)) return (false, "not a git repo", new());
        return (true, "", Git.ChangedFiles(dir));
    }

    /// <summary>Recent commits in a mod's repo (newest first).</summary>
    public (bool ok, string error, List<Git.Commit> commits) Log(string mod, int count = 20)
    {
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return (false, err, new());
        if (!Git.IsRepo(dir)) return (false, "not a git repo", new());
        return (true, "", Git.Log(dir, count));
    }

    /// <summary>Diff of a mod's work tree vs HEAD (optionally one file).</summary>
    public (bool ok, string error, string diff) Diff(string mod, string? file = null)
    {
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return (false, err, "");
        if (!Git.IsRepo(dir)) return (false, "not a git repo", "");
        return (true, "", Git.Diff(dir, file));
    }

    /// <summary>Stage + commit a mod's changes. <paramref name="all"/> stages everything first; otherwise only
    /// the already-staged index is committed.</summary>
    public OpResult Commit(string mod, string message, bool all = true)
    {
        if (string.IsNullOrWhiteSpace(message)) return new OpResult(false, "commit message required");
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return new OpResult(false, err);
        if (!Git.IsRepo(dir)) return new OpResult(false, "not a git repo");
        if (all) { var s = Git.StageAll(dir); if (!s.ok) return new OpResult(false, s.msg); }
        var r = Git.CommitStaged(dir, message);
        return new OpResult(r.ok, r.msg);
    }

    /// <summary>Current branch + all local branches of a mod's repo.</summary>
    public (bool ok, string error, string current, List<string> branches) Branches(string mod)
    {
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return (false, err, "", new());
        if (!Git.IsRepo(dir)) return (false, "not a git repo", "", new());
        var (cur, all) = Git.Branches(dir);
        return (true, "", cur, all);
    }

    private OpResult OnRepo(string mod, Func<string, (bool ok, string msg)> op)
    {
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return new OpResult(false, err);
        if (!Git.IsRepo(dir)) return new OpResult(false, "not a git repo");
        var (ok, msg) = op(dir);
        return new OpResult(ok, msg);
    }

    public OpResult Checkout(string mod, string branch) => OnRepo(mod, d => Git.Checkout(d, branch));
    public OpResult CreateBranch(string mod, string name) => OnRepo(mod, d => Git.CreateBranch(d, name));
    public OpResult Push(string mod) => OnRepo(mod, Git.Push);
    public OpResult Pull(string mod) => OnRepo(mod, Git.Pull);

    /// <summary>Cut a GitHub release with full options, optionally uploading the built <c>@&lt;mod&gt;</c> PBOs
    /// as release assets.</summary>
    public OpResult Release(string mod, ReleaseOptions opts, bool attachBuiltAddons)
    {
        if (string.IsNullOrWhiteSpace(opts.Tag)) return new OpResult(false, "tag required (e.g. v1.0.0)");
        var dir = ResolveProject(mod, out var err);
        if (dir is null) return new OpResult(false, err);
        if (!GitHub.IsAvailable()) return new OpResult(false, "gh (GitHub CLI) not found");
        if (!Git.IsRepo(dir) || !Git.HasCommits(dir)) return new OpResult(false, "not a published repo yet — publish first");

        List<string>? assets = null;
        if (attachBuiltAddons)
        {
            var root = ProjectPaths.Root(Profiles.ResolveActive(_configPath).cfg);
            var addons = ProjectPaths.BuildAddonsDir(root, mod);
            if (Directory.Exists(addons))
            {
                try { assets = Directory.GetFiles(addons, "*.pbo").ToList(); } catch { /* ignore */ }
                if (assets is { Count: 0 }) assets = null;
            }
        }

        var title = string.IsNullOrWhiteSpace(opts.Title) ? $"{mod} {opts.Tag}" : opts.Title;
        var (ok, msg) = GitHub.Release(dir, opts with { Title = title }, assets);
        var assetNote = assets is { Count: > 0 } ? $" (+{assets.Count} asset{(assets.Count > 1 ? "s" : "")})" : "";
        return new OpResult(ok, ok ? $"released {opts.Tag} → {msg}{assetNote}" : $"gh release failed: {msg}");
    }
}
