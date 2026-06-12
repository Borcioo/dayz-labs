using System.CommandLine;
using Dzl.Core.App;

namespace Dzl.Cli.Commands;

/// <summary>repo (status / publish / release).</summary>
internal static class RepoCommands
{
    public static Command Repo(CliContext c)
    {
        var repoCmd = new Command("repo", "Manage a mod project as a git/GitHub repo.");

        var repoStatusArg = new Argument<string>("Mod", "Mod project name.");
        var repoStatusCmd = new Command("status", "Show git status (branch, ahead/behind, dirty, remote).") { repoStatusArg };
        repoStatusCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(repoStatusArg);
            var s = new RepoService(configPath).Status(mod);
            if (!s.IsRepo) { Console.WriteLine($"{mod}: {s.Detail}"); return; }
            var ab = (s.Ahead > 0 || s.Behind > 0) ? $"  ↑{s.Ahead} ↓{s.Behind}" : "";
            var remote = s.HasRemote ? "" : "  (no remote)";
            Console.WriteLine($"{mod}: {s.Branch}  {s.Detail}{ab}{remote}");
        });
        repoCmd.AddCommand(repoStatusCmd);

        var repoPubArg = new Argument<string>("Mod", "Mod project name.");
        var repoPubPublic = new Option<bool>("--public", "Make the repo public (default: private).");
        var repoPubDesc = new Option<string?>("--description", () => null, "Repo description.");
        var repoPublishCmd = new Command("publish", "Init git + create & push a GitHub repo for the mod.") { repoPubArg, repoPubPublic, repoPubDesc };
        repoPublishCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(repoPubArg);
            var pub = new RepoService(configPath).Publish(mod,
                @private: !ctx.ParseResult.GetValueForOption(repoPubPublic),
                description: ctx.ParseResult.GetValueForOption(repoPubDesc));
            CliOut.Result(ctx, pub.Ok, pub.Message);
        });
        repoCmd.AddCommand(repoPublishCmd);

        var repoRelArg = new Argument<string>("Mod", "Mod project name.");
        var repoRelTag = new Argument<string>("tag", "Release tag (e.g. v1.0.0).");
        var repoRelNotes = new Option<string?>("--notes", () => null, "Release notes (omit = GitHub auto-generated).");
        var repoReleaseCmd = new Command("release", "Cut a GitHub release at HEAD for the mod.") { repoRelArg, repoRelTag, repoRelNotes };
        repoReleaseCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(repoRelArg);
            var tag = ctx.ParseResult.GetValueForArgument(repoRelTag);
            var rel = new RepoService(configPath).Release(mod, tag, ctx.ParseResult.GetValueForOption(repoRelNotes));
            CliOut.Result(ctx, rel.Ok, rel.Message);
        });
        repoCmd.AddCommand(repoReleaseCmd);
        return repoCmd;
    }
}
