using System.CommandLine;
using Dzl.Core.App;

namespace Dzl.Cli.Commands;

/// <summary>workshop (search / add / update) — Steam Workshop.</summary>
internal static class WorkshopCommands
{
    public static Command Workshop(CliContext c)
    {
        var workshopCmd = new Command("workshop", "Search + download Steam Workshop mods (search needs a Web API key; download needs steamcmd).");

        var wsSearchQ = new Argument<string>("query", "Search text.");
        var wsSearchCount = new Option<int>("--count", () => 20, "Max results.");
        var wsSearchCmd = new Command("search", "Search the Workshop (Steam Web API).") { wsSearchQ, wsSearchCount };
        wsSearchCmd.SetHandler(async ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var q = ctx.ParseResult.GetValueForArgument(wsSearchQ);
            var count = ctx.ParseResult.GetValueForOption(wsSearchCount);
            var (ok, error, items) = await new WorkshopService(configPath).SearchAsync(q, count);
            if (!ok) { CliOut.Fail(ctx, error); return; }
            CliOut.List(items, "(no results)", i => $"{i.Id,-12} {i.Title}");
        });
        workshopCmd.AddCommand(wsSearchCmd);

        var wsAddId = new Argument<string>("id", "Workshop published-file id.");
        var wsAddCmd = new Command("add", "Download a Workshop item via steamcmd (opens a console for login).") { wsAddId };
        wsAddCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var r = new WorkshopService(configPath).Download(ctx.ParseResult.GetValueForArgument(wsAddId));
            CliOut.Result(ctx, r.Ok, r.Message);
        });
        workshopCmd.AddCommand(wsAddCmd);

        var wsUpdId = new Argument<string?>("id", () => null, "Item id (omit = re-download all downloaded items).");
        var wsUpdateCmd = new Command("update", "Re-download item(s) to update them (steamcmd).") { wsUpdId };
        wsUpdateCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var svc = new WorkshopService(configPath);
            var id = ctx.ParseResult.GetValueForArgument(wsUpdId);
            var ids = id is not null ? new List<string> { id } : svc.Downloaded();
            CliOut.List(ids, "(nothing downloaded to update)", x => svc.Download(x).Message);
        });
        workshopCmd.AddCommand(wsUpdateCmd);
        return workshopCmd;
    }
}
