namespace Dzl.Core.Config;

/// <summary>
/// Machine-global settings — the local DayZ install, tools, projects root, executable names,
/// mod scan-roots and app prefs. These do NOT vary per server. Persisted in <c>config.json</c>.
/// Per-server settings live in <see cref="InstanceConfig"/> (one file per server under <c>instances/</c>).
/// Defaults mirror <see cref="DzlConfig"/> (a round-trip test guards against drift).
/// </summary>
public sealed record GlobalConfig
{
    public string DayzPath { get; init; } = @"E:\Steam\steamapps\common\DayZ";
    public string DayzToolsPath { get; init; } = @"E:\Steam\steamapps\common\DayZ Tools";

    /// <summary>Single home for everything dzl creates — mod source projects at
    /// <c>&lt;ProjectsRoot&gt;\&lt;Mod&gt;</c> and server instances at <c>&lt;ProjectsRoot&gt;\servers\&lt;instance&gt;</c>.
    /// Empty = resolve to <c>%USERPROFILE%\DayZProjects</c> (see <c>ProjectPaths.Root</c>). snake_case: projects_root.</summary>
    public string ProjectsRoot { get; init; } = "";

    public string ExeDebug { get; init; } = "DayZDiag_x64.exe";
    public string ExeNormal { get; init; } = "DayZServer_x64.exe";
    public string ClientExeDebug { get; init; } = "DayZDiag_x64.exe";
    public string ClientExeNormal { get; init; } = "DayZ_x64.exe";
    public List<string> ScanRoots { get; init; } = new() { @"P:\@Dependencies", @"P:\@PackedMods", @"P:\" };
    public List<string> LogsShown { get; init; } = new() { "script", "rpt", "adm", "client" };
    public int ModWidthIdx { get; init; }

    /// <summary>When true the tray hosts the named-pipe automation server so the dzl CLI and the
    /// Claude MCP integration can drive this process. Off by default. snake_case: enable_automation_server.</summary>
    public bool EnableAutomationServer { get; init; } = false;

    /// <summary>When a server is started from CLI/MCP and the tray isn't running, auto-launch it
    /// (hidden, as a monitor). Default on. snake_case: auto_launch_tray.</summary>
    public bool AutoLaunchTray { get; init; } = true;

    /// <summary>Name of the active server instance (replaces the old <c>active_preset</c>). snake_case: active_instance.</summary>
    public string ActiveInstance { get; init; } = "";
}
