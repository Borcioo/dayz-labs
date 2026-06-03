namespace Dzl.Tray;

/// <summary>
/// Central glossary of short, accurate explanations for DayZ dev-launcher options.
/// Surfaced in the UI through <see cref="InfoTip"/> (the ⓘ icons). Keep entries
/// concise and beginner-friendly — they teach WHAT a thing is and WHY it's set.
/// </summary>
public static class HelpText
{
    public const string Mode =
        "Debug vs Normal. Debug launches DayZDiag_x64.exe as the server with -filePatching, loading your unpacked mod scripts straight from disk — edit a script and reload without repacking PBOs. Use it while developing. Normal launches the dedicated DayZServer_x64.exe with packed PBOs, for production.";

    public const string FilePatching =
        "-filePatching loads loose, unpacked script files from your mod folders instead of from packed PBOs, so you can iterate on Enforce Script without rebuilding. Dev only — production runs packed, signed PBOs.";

    public const string ModSide =
        "Where a mod loads: both = -mod (server AND client; most content mods). server = -serverMod (server-only, e.g. admin tools/server logic). client = client -mod only (UI/visual mods).";

    public const string ModEnabled =
        "Tick to include this mod in the launch command. Order matters — mods load top to bottom, so dependencies (e.g. @CF) must sit above mods that need them.";

    public const string ModOrder =
        "Load order. Mods load top-to-bottom; put frameworks/dependencies (like @CF) above the mods that require them. Drag to reorder, or use the buttons.";

    public const string Profile =
        "A profile is a full snapshot of your setup — paths, port, selected mods + order, and launch params. Make one per project/map and switch between them. The active profile is what Start uses; it's remembered between sessions.";

    public const string Port =
        "-port: the port the server listens on (default 2302). Change it to run two servers at once or to avoid a conflict.";

    public const string ProfilesDir =
        "-profiles: a folder where the server writes its logs (script_*.log, .RPT, .ADM) and runtime data. The client uses a SEPARATE profiles folder so their logs don't collide.";

    public const string Mission =
        "The mission/map the server runs, e.g. dayzOffline.chernarusplus — a folder under the DayZ install's mpmissions\\.";

    public const string ConfigName =
        "The server config file (serverDZ.cfg): hostname, max players, time settings, signature verification, and which mission to load.";

    public const string ConnectIp =
        "-connect: the address the client auto-connects to on launch. 127.0.0.1 means the local server you just started.";

    public const string PlayerName =
        "-name: the in-game profile/player name the client launches with.";

    public const string VerifySignatures =
        "serverDZ.cfg verifySignatures: 0 accepts unsigned mods (convenient for dev). Production should use 2 and sign mods with a .bikey so only verified mods load.";

    public const string WorkDrive =
        "DayZ Tools maps a 'work drive' (P:) — a folder on your disk mounted as the P: letter. Your mod sources and the extracted vanilla game data live there so tools and configs resolve paths the way Bohemia's pipeline expects.";

    public const string GameData =
        "Extract Game Data unpacks Bohemia's vanilla PBOs into P: so you can reference/inspect vanilla configs, models and scripts while building mods. One-time — re-run after a game update.";

    public const string PerModeParams =
        "Extra launch flags appended to the command, kept PER MODE: your debug set (e.g. -filePatching, -scriptDebug=true) stays separate from your normal/production set. Edit each set independently; Reset restores that mode's defaults.";

    public const string ScanRoots =
        "Folders dzl scans for mods. A folder counts as a mod if it has an addons\\ directory, OR a config.cpp plus a scripts\\ folder — so both packed mods and unpacked dev mods are found.";

    public const string Automation =
        "The automation server is a local pipe that lets the dzl CLI and the Claude MCP integration drive this app (start/stop, read logs, switch profiles). Off = no background listener; turn it on if you use the CLI or MCP. Applies on next launch.";

    public const string Mcp =
        "MCP (Model Context Protocol) lets Claude Code control dzl in natural language. Register the dzl MCP server in Claude, then ask it to start the server, read logs, pack PBOs, etc.";

    public const string DiagServer =
        "In development you don't need the separate dedicated DayZ Server: DayZDiag_x64.exe -server IS the server, with hot-reload. The dedicated DayZServer (from Steam) is only for production hosting.";

    public const string Scaffold =
        "Scaffolds a runnable server instance: writes a dev serverDZ.cfg (if absent), creates the profiles/ and profiles_client/ folders, and copies the mission so the server has something to load.";

    public const string Flags =
        "Common flags — -dologs: enable logging; -adminLog: write admin actions to the .ADM log; -freezecheck: server watchdog that detects hangs; -scriptDebug=true: in-game script debugger (dev); -window: run the client windowed (handy beside the editor); -nosplash: skip intro videos for a faster launch.";
}
