using System.CommandLine;
using Dzl.Core.App;
using Dzl.Core.Env;
using Dzl.Core.Projects;

namespace Dzl.Cli.Commands;

/// <summary>mods (projects), new, import, link.</summary>
internal static class ProjectsCommands
{
    public static Command Mods(CliContext c)
    {
        var modsCmd = new Command("mods", "List the current ordered mod selection.");
        modsCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            foreach (var m in cfg.Mods)
            {
                var box = m.Enabled ? "[x]" : "[ ]";
                var tag = m.Side == "both" ? "" : $"  ({m.Side})";
                Console.WriteLine($"{box} {m.Path}{tag}");
            }
        });

        var modsProjectsCmd = new Command("projects", "List mod source projects under ProjectsRoot.");
        modsProjectsCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var projectsRoot = ProjectPaths.Root(cfg);
            var projects = ModProjects.Discover(projectsRoot, EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath));
            CliOut.List(projects, "(no mod projects)",
                p => $"{p.Name}  [{(p.Linked ? "linked" : "unlinked")}]  {p.Path}");
        });
        modsCmd.AddCommand(modsProjectsCmd);
        return modsCmd;
    }

    public static Command New(CliContext c)
    {
        var newModArg = new Argument<string>("Mod", "Mod name (letters, digits, underscores; start with letter).");
        var newAuthorOpt = new Option<string?>("--author", () => null, "Author handle (cached for future use).");
        var newGithubOpt = new Option<bool>("--github", "After scaffolding, init git + create & push a GitHub repo (gh).");
        var newPublicOpt = new Option<bool>("--public", "With --github, make the repo public (default: private).");
        var newCmd = new Command("new", "Scaffold a new DayZ mod source project.") { newModArg, newAuthorOpt, newGithubOpt, newPublicOpt };
        newCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, configPath) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(newModArg);
            var authorOpt = ctx.ParseResult.GetValueForOption(newAuthorOpt);
            var configDir = Path.GetDirectoryName(configPath)!;
            var author = authorOpt ?? ModScaffold.CachedAuthor(configDir);
            if (author is null)
            {
                CliOut.Fail(ctx, "no author; pass --author <handle> once to cache it");
                return;
            }
            var projectsRoot = ProjectPaths.Root(cfg);
            var result = ModScaffold.Scaffold(projectsRoot, mod, author);
            Console.WriteLine(result.Message);
            if (authorOpt is not null) ModScaffold.SaveAuthor(configDir, authorOpt);
            if (result.Ok)
            {
                var linkResult = Junction.Ensure(
                    ProjectPaths.JunctionPath(EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath), mod),
                    ProjectPaths.ModDir(projectsRoot, mod));
                if (!linkResult.Ok)
                    Console.WriteLine($"warning: link {linkResult.Action}: {linkResult.Detail}");
                else
                    Console.WriteLine($"{linkResult.Action}: {linkResult.Detail}");

                if (ctx.ParseResult.GetValueForOption(newGithubOpt))
                {
                    var pub = new RepoService(configPath).Publish(mod, @private: !ctx.ParseResult.GetValueForOption(newPublicOpt));
                    CliOut.Result(ctx, pub.Ok, pub.Message);
                }
            }
            else
            {
                ctx.ExitCode = 1;
            }
        });
        return newCmd;
    }

    public static Command Import(CliContext c)
    {
        var importPathArg = new Argument<string>("path", "Path to an existing mod source folder.");
        var importNameOpt = new Option<string?>("--name", () => null, "Override the mod name (defaults to folder name).");
        var importCmd = new Command("import", "Import an existing mod source folder into ProjectsRoot.") { importPathArg, importNameOpt };
        importCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var path = ctx.ParseResult.GetValueForArgument(importPathArg);
            var name = ctx.ParseResult.GetValueForOption(importNameOpt);
            var result = ModImport.Import(ProjectPaths.Root(cfg), path, name,
                EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath));
            CliOut.Result(ctx, result.Ok, result.Message);
        });
        return importCmd;
    }

    public static Command Link(CliContext c)
    {
        var linkModArg = new Argument<string>("Mod", "Mod name to link on P:.");
        var linkCmd = new Command("link", "Create or repair the P:\\ junction for a mod.") { linkModArg };
        linkCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var mod = ctx.ParseResult.GetValueForArgument(linkModArg);
            var projectsRoot = ProjectPaths.Root(cfg);
            var result = Junction.Ensure(
                ProjectPaths.JunctionPath(EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath), mod),
                ProjectPaths.ModDir(projectsRoot, mod));
            CliOut.Result(ctx, result.Ok, $"{result.Action}: {result.Detail}");
        });
        return linkCmd;
    }
}
