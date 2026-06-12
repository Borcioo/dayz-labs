using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;

namespace Dzl.Core.Ipc;

public static class IpcDispatcher
{
    private static string J(object o) => JsonSerializer.Serialize(o, ConfigStore.Json);

    public static IpcResponse Handle(IpcRequest req, LauncherService svc)
    {
        try
        {
            return IpcMethods.Table.TryGetValue(req.Method, out var call)
                ? new IpcResponse(true, null, J(call(svc, req)))
                : new IpcResponse(false, $"unknown method: {req.Method}", null);
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, ex.Message, null);
        }
    }
}
