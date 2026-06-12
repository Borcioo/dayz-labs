using System.CommandLine;
using Dzl.Cli;
using Dzl.Cli.Commands;

var context = new CliContext();
var root = new RootCommand("dzl - DayZ dev launcher");
root.AddGlobalOption(context.ConfigOption);

// Registration order is load-bearing: root --help lists commands in add order.
Command[] commands =
{
    ProjectsCommands.Mods(context),
    LifecycleCommands.Start(context),
    LifecycleCommands.Stop(context),
    LifecycleCommands.Restart(context),
    LogsStatusCommands.Logs(context),
    LogsStatusCommands.Status(context),
    ConfigCommands.Config(context),
    PresetCommands.Preset(context),
    ToolsCommands.Tools(context),
    ToolsCommands.Paa(context),
    ToolsCommands.Pack(context),
    ToolsCommands.Derap(context),
    ServerCommands.Server(context),
    ServerCommands.Base(context),
    ToolsCommands.Workdrive(context),
    ProjectsCommands.New(context),
    ProjectsCommands.Import(context),
    ProjectsCommands.Link(context),
    BuildCommands.Build(context),
    BuildCommands.Preflight(context),
    BuildCommands.Key(context),
    RepoCommands.Repo(context),
    TypesCommands.Types(context),
    WorkshopCommands.Workshop(context),
};
foreach (var cmd in commands) root.AddCommand(cmd);

return await root.InvokeAsync(args);
