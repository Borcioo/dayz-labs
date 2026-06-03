using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Dzl.Core.Ipc;

public static class PipeClient
{
    // Returns null if no server is listening within the timeout.
    // pipeName defaults to the shared IpcContract.PipeName; tests pass a unique name
    // so they don't accidentally connect to a real tray instance that's running.
    public static IpcResponse? Send(IpcRequest req, int timeoutMs = 300, string? pipeName = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? IpcContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(timeoutMs);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(req, IpcContract.Json));
            var line = reader.ReadLine();
            return line is null ? null : JsonSerializer.Deserialize<IpcResponse>(line, IpcContract.Json);
        }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }
    }

    public static bool IsServerUp(string? pipeName = null) => Send(new IpcRequest("status", null), pipeName: pipeName) is not null;
}
