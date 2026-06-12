using Dzl.Core.App;
using Dzl.Core.Json;

namespace Dzl.Core.Ipc;

public static class IpcDispatcher
{
    public static IpcResponse Handle(IpcRequest req, LauncherService svc)
    {
        try
        {
            return IpcMethods.Table.TryGetValue(req.Method, out var call)
                ? new IpcResponse(true, null, DzlJson.Serialize(call(svc, req)))
                : new IpcResponse(false, $"unknown method: {req.Method}", null);
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, ex.Message, null);
        }
    }
}
