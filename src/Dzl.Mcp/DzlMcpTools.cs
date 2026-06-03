using System.ComponentModel;
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;
using ModelContextProtocol.Server;

namespace Dzl.Mcp;

[McpServerToolType]
public static class DzlMcpTools
{
    private static string ConfigPath() =>
        Environment.GetEnvironmentVariable("DZL_CONFIG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dzl", "config.json");

    private static LauncherService Svc() => new(ConfigPath());
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    [McpServerTool, Description("Get running state, mode, port, active profile, paths, enabled mods and newest log files.")]
    public static string Status() => J(Svc().Status());

    [McpServerTool, Description("List the enabled mods (path + side) of the active profile.")]
    public static string ListMods() => J(Svc().Mods());

    [McpServerTool, Description("List profiles/presets; the active one is flagged.")]
    public static string ListPresets() => J(Svc().Presets());

    [McpServerTool, Description("Switch the active profile by name.")]
    public static string SetPreset([Description("Preset name")] string name) => J(Svc().SetPreset(name));

    [McpServerTool, Description("Read the last N lines of a log: script|rpt|adm|client.")]
    public static string Logs([Description("script|rpt|adm|client")] string which,
                              [Description("How many trailing lines")] int lines = 50)
        => J(Svc().Logs(which, lines));

    [McpServerTool, Description("Start the server (and optionally the client). mode = debug|normal.")]
    public static string Start([Description("debug|normal")] string mode = "debug",
                               [Description("also start the client")] bool client = false)
        => J(Svc().Start(mode, client, "mcp"));

    [McpServerTool, Description("Stop the server (and optionally the client).")]
    public static string Stop([Description("also stop the client")] bool client = false) => J(Svc().Stop(client, "mcp"));

    [McpServerTool, Description("Restart the server. mode = debug|normal.")]
    public static string Restart([Description("debug|normal")] string mode = "debug") => J(Svc().Restart(mode, "mcp"));
}
