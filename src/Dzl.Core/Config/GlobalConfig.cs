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

    /// <summary>Mount the P: work drive when the tray app launches. Off by default. snake_case: automount_work_drive.</summary>
    public bool AutomountWorkDrive { get; init; } = false;

    /// <summary>Override for the work-drive source folder (the always-live folder P: is mounted from /
    /// junctions are anchored on). Empty = auto-derive from DayZ Tools settings.ini. snake_case: work_drive_source.</summary>
    public string WorkDriveSource { get; init; } = "";

    /// <summary>Folder holding signing keys (.biprivatekey/.bikey). Empty = <c>&lt;ProjectsRoot&gt;\keys</c>.
    /// snake_case: keys_dir.</summary>
    public string KeysDir { get; init; } = "";

    /// <summary>Name of the creator's signing key (one key signs all your mods). Empty = fall back to the
    /// cached author handle. snake_case: signing_key.</summary>
    public string SigningKey { get; init; } = "";

    /// <summary>Code editor launcher (exe/cli) for "Open in editor" on mods + servers, e.g. cursor / code.
    /// Empty = no editor configured. snake_case: editor_path.</summary>
    public string EditorPath { get; init; } = "";

    /// <summary>Steam Web API key (steamcommunity.com/dev/apikey) — required for Workshop search. snake_case: steam_api_key.</summary>
    public string SteamApiKey { get; init; } = "";

    /// <summary>Path to steamcmd.exe — required for Workshop download/update. snake_case: steamcmd_path.</summary>
    public string SteamCmdPath { get; init; } = "";

    /// <summary>steamcmd login username for Workshop downloads (empty = anonymous; owned/DayZ items usually
    /// need a login — password/Steam Guard are entered in the spawned console). snake_case: steam_login.</summary>
    public string SteamLogin { get; init; } = "";

    /// <summary>Steam web access token (JWT) for in-app Subscribe/Unsubscribe via IPublishedFileService.
    /// From a logged-in Steam web session; expires periodically. Empty = Subscribe opens the Steam page.
    /// snake_case: steam_access_token.</summary>
    public string SteamAccessToken { get; init; } = "";

    /// <summary>Name of the active server instance (replaces the old <c>active_preset</c>). snake_case: active_instance.</summary>
    public string ActiveInstance { get; init; } = "";
}
