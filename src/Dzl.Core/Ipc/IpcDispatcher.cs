using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Core.Ipc;

public static class IpcDispatcher
{
    private static string Arg(IpcRequest r, string k, string dflt = "") =>
        r.Args is not null && r.Args.TryGetValue(k, out var v) ? v : dflt;
    private static bool Flag(IpcRequest r, string k) =>
        bool.TryParse(Arg(r, k, "false"), out var b) && b;
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    public static IpcResponse Handle(IpcRequest req, LauncherService svc)
    {
        try
        {
            return req.Method switch
            {
                "status"       => new(true, null, J(svc.Status())),
                "mods"         => new(true, null, J(svc.Mods())),
                "presets"      => new(true, null, J(svc.Presets())),
                "set_preset"   => new(true, null, J(svc.SetPreset(Arg(req, "name")))),
                "save_preset"  => new(true, null, J(svc.SaveActivePresetAs(Arg(req, "name")))),
                "logs"         => new(true, null, J(svc.Logs(Arg(req, "which", "script"),
                                      int.TryParse(Arg(req, "lines", "50"), out var n) ? n : 50))),
                "start"        => new(true, null, J(svc.Start(Arg(req, "mode", "debug"), Flag(req, "client"), Arg(req, "source", "cli")))),
                "stop"         => new(true, null, J(svc.Stop(Flag(req, "client")))),
                "restart"      => new(true, null, J(svc.Restart(Arg(req, "mode", "debug"), Arg(req, "source", "cli")))),
                _              => new(false, $"unknown method: {req.Method}", null),
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, ex.Message, null);
        }
    }
}
