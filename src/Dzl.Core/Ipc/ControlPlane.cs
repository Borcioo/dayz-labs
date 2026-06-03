using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Core.Ipc;

// Routes through the tray's pipe when it's up, else operates directly.
public sealed class ControlPlane
{
    private readonly string _configPath;
    private readonly string? _pipeName;
    // pipeName defaults to the shared pipe; tests pass a unique name to force the direct path.
    public ControlPlane(string configPath, string? pipeName = null) { _configPath = configPath; _pipeName = pipeName; }
    private LauncherService Direct() => new(_configPath);
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    private string Route(IpcRequest req, Func<LauncherService, object> direct)
    {
        var resp = PipeClient.Send(req, pipeName: _pipeName);
        if (resp is not null && resp.Ok && resp.Json is not null) return resp.Json;
        return J(direct(Direct()));
    }

    public string StatusJson() => Route(new("status", null), s => s.Status());
    public string ModsJson() => Route(new("mods", null), s => s.Mods());
    public string PresetsJson() => Route(new("presets", null), s => s.Presets());
    public string SetPresetJson(string name) => Route(new("set_preset", new() { ["name"] = name }), s => s.SetPreset(name));
    public string SavePresetJson(string name) => Route(new("save_preset", new() { ["name"] = name }), s => s.SaveActivePresetAs(name));
    public string LogsJson(string which, int lines) =>
        Route(new("logs", new() { ["which"] = which, ["lines"] = lines.ToString() }), s => s.Logs(which, lines));
    public string StartJson(string mode, bool client, string source) =>
        Route(new("start", new() { ["mode"] = mode, ["client"] = client.ToString(), ["source"] = source }), s => s.Start(mode, client, source));
    public string StopJson(bool client) =>
        Route(new("stop", new() { ["client"] = client.ToString() }), s => s.Stop(client));
    public string RestartJson(string mode, string source) =>
        Route(new("restart", new() { ["mode"] = mode, ["source"] = source }), s => s.Restart(mode, source));
}
