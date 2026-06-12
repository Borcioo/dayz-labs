using System.Text.Json;
using Dzl.Core.Json;

namespace Dzl.Core.Ipc;

public sealed record IpcRequest(string Method, Dictionary<string, string>? Args);
public sealed record IpcResponse(bool Ok, string? Error, string? Json);

public static class IpcContract
{
    public const string PipeName = "dzl-ipc-v1";
    // MUST stay WriteIndented=false (DzlJson.Snake): the pipe protocol is line-framed —
    // one JSON document per line — so an indented payload would split across reads.
    public static readonly JsonSerializerOptions Json = DzlJson.Snake;
}
