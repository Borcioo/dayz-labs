using System.Text.Json;

namespace Dzl.Core.Ipc;

public sealed record IpcRequest(string Method, Dictionary<string, string>? Args);
public sealed record IpcResponse(bool Ok, string? Error, string? Json);

public static class IpcContract
{
    public const string PipeName = "dzl-ipc-v1";
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}
