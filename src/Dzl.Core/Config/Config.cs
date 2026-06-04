namespace Dzl.Core.Config;

public sealed record DzlConfig
{
    public string DayzPath { get; init; } = @"E:\Steam\steamapps\common\DayZ";
    public string DayzToolsPath { get; init; } = @"E:\Steam\steamapps\common\DayZ Tools";
    public string ProfilesPath { get; init; } = @"E:\Steam\steamapps\common\DayZ\profiles";
    public string ClientProfilesPath { get; init; } = @"E:\Steam\steamapps\common\DayZ\profiles_client";
    /// <summary>Single home for everything dzl creates — mod source projects at
    /// <c>&lt;ProjectsRoot&gt;\&lt;Mod&gt;</c> and server instances at <c>&lt;ProjectsRoot&gt;\servers\&lt;instance&gt;</c>.
    /// Empty = resolve to <c>%USERPROFILE%\DayZProjects</c> (see <c>ProjectPaths.Root</c>). snake_case: projects_root.</summary>
    public string ProjectsRoot { get; init; } = "";

    public string ExeDebug { get; init; } = "DayZDiag_x64.exe";
    public string ExeNormal { get; init; } = "DayZServer_x64.exe";
    public string ClientExeDebug { get; init; } = "DayZDiag_x64.exe";
    public string ClientExeNormal { get; init; } = "DayZ_x64.exe";
    public List<string> ScanRoots { get; init; } = new() { @"P:\@Dependencies", @"P:\@PackedMods", @"P:\" };
    public int Port { get; init; } = 2302;
    public string Mission { get; init; } = "./mpmissions/dayzOffline.chernarusplus";
    public string PlayerName { get; init; } = "DevMacie";
    public string ConfigName { get; init; } = "serverDZ.cfg";
    public string ConnectIp { get; init; } = "127.0.0.1";
    public List<ModEntry> Mods { get; init; } = new();
    public List<string> LogsShown { get; init; } = new() { "script", "rpt", "adm", "client" };
    public string Mode { get; init; } = "debug";
    public int ModWidthIdx { get; init; }
    public string ActivePreset { get; init; } = "";
    public List<string> ServerParamsDebug { get; init; } = new() { "-filePatching", "-dologs", "-adminLog", "-freezecheck" };
    public List<string> ServerParamsNormal { get; init; } = new() { "-dologs", "-adminLog", "-freezecheck" };
    public List<string> ClientParamsDebug { get; init; } = new() { "-window", "-nosplash", "-filePatching", "-doLogs", "-scriptDebug=true" };
    public List<string> ClientParamsNormal { get; init; } = new() { "-window", "-nosplash" };

    /// <summary>
    /// When true the tray hosts the named-pipe automation server so the dzl CLI and the
    /// Claude MCP integration can drive this process. Off by default (opt-in); when off no
    /// background pipe listener is started. .NET-only field (snake_case: enable_automation_server).
    /// </summary>
    public bool EnableAutomationServer { get; init; } = false;

    /// <summary>When a server is started from CLI/MCP and the tray isn't running, auto-launch it
    /// (hidden, as a monitor). Default on. snake_case: auto_launch_tray.</summary>
    public bool AutoLaunchTray { get; init; } = true;

    public static DzlConfig Default() => new();
}

public sealed record ModEntry
{
    public string Path { get; init; } = "";
    public bool Enabled { get; init; }
    public string Side { get; init; } = "both"; // both|server|client
}
