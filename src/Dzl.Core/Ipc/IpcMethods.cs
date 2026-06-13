using Dzl.Core.App;

namespace Dzl.Core.Ipc;

/// <summary>Single source of truth for the routed IPC surface: the dispatch table binds each method
/// name to its <see cref="LauncherService"/> call exactly once.</summary>
/// <remarks><see cref="IpcDispatcher"/> (tray side) and <see cref="ControlPlane"/>'s direct fallback
/// both execute the same entry, so the two paths can't drift. Adding a method = one const + one table
/// entry, then surface it in the frontends.</remarks>
public static class IpcMethods
{
    public const string Status = "status";
    public const string Mods = "mods";
    public const string Presets = "presets";
    public const string SetPreset = "set_preset";
    public const string SavePreset = "save_preset";
    public const string Logs = "logs";
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Restart = "restart";

    public static readonly IReadOnlyDictionary<string, Func<LauncherService, IpcRequest, object>> Table =
        new Dictionary<string, Func<LauncherService, IpcRequest, object>>
        {
            [Status]     = (s, _) => s.Status(),
            [Mods]       = (s, _) => s.Mods(),
            [Presets]    = (s, _) => s.Presets(),
            [SetPreset]  = (s, r) => s.SetPreset(r.Arg("name")),
            [SavePreset] = (s, r) => s.SaveActivePresetAs(r.Arg("name")),
            [Logs]       = (s, r) => s.Logs(r.Arg("which", "script"), r.ArgInt("lines", 50)),
            [Start]      = (s, r) => s.Start(r.Arg("mode", "debug"), r.Flag("client"), r.Arg("source", "cli")),
            [Stop]       = (s, r) => s.Stop(r.Flag("client")),
            [Restart]    = (s, r) => s.Restart(r.Arg("mode", "debug"), r.Arg("source", "cli")),
        };
}

/// <summary>String-arg accessors shared by the dispatch table and any future request handling.</summary>
public static class IpcRequestArgs
{
    public static string Arg(this IpcRequest r, string key, string dflt = "") =>
        r.Args is not null && r.Args.TryGetValue(key, out var v) ? v : dflt;

    public static int ArgInt(this IpcRequest r, string key, int dflt) =>
        int.TryParse(r.Arg(key), out var n) ? n : dflt;

    public static bool Flag(this IpcRequest r, string key) =>
        bool.TryParse(r.Arg(key, "false"), out var b) && b;
}
