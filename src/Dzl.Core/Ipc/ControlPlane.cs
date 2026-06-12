using Dzl.Core.App;
using Dzl.Core.Json;

namespace Dzl.Core.Ipc;

// Routes through the tray's pipe when it's up, else operates directly. The direct fallback runs
// the same IpcMethods table entry the tray-side dispatcher would, so the two paths can't drift.
public sealed class ControlPlane
{
    private readonly string _configPath;
    private readonly string? _pipeName;
    // pipeName defaults to the shared pipe; tests pass a unique name to force the direct path.
    public ControlPlane(string configPath, string? pipeName = null) { _configPath = configPath; _pipeName = pipeName; }

    private string Route(IpcRequest req)
    {
        var resp = PipeClient.Send(req, pipeName: _pipeName);
        if (resp is not null && resp.Ok && resp.Json is not null) return resp.Json;
        return DzlJson.Serialize(IpcMethods.Table[req.Method](new LauncherService(_configPath), req));
    }

    public string StatusJson() => Route(new(IpcMethods.Status, null));
    public string ModsJson() => Route(new(IpcMethods.Mods, null));
    public string PresetsJson() => Route(new(IpcMethods.Presets, null));
    public string SetPresetJson(string name) => Route(new(IpcMethods.SetPreset, new() { ["name"] = name }));
    public string SavePresetJson(string name) => Route(new(IpcMethods.SavePreset, new() { ["name"] = name }));
    public string LogsJson(string which, int lines) =>
        Route(new(IpcMethods.Logs, new() { ["which"] = which, ["lines"] = lines.ToString() }));
    public string StartJson(string mode, bool client, string source) =>
        Route(new(IpcMethods.Start, new() { ["mode"] = mode, ["client"] = client.ToString(), ["source"] = source }));
    public string StopJson(bool client) =>
        Route(new(IpcMethods.Stop, new() { ["client"] = client.ToString() }));
    public string RestartJson(string mode, string source) =>
        Route(new(IpcMethods.Restart, new() { ["mode"] = mode, ["source"] = source }));
}
