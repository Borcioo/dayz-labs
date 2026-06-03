using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Dzl.Core.App;

namespace Dzl.Core.Ipc;

// Hosts the dispatcher on a named pipe. One request line -> one response line.
// Run on a background task from the tray; cancel via the token.
public sealed class PipeServer
{
    private readonly Func<LauncherService> _svc;
    public PipeServer(Func<LauncherService> svc) { _svc = svc; }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(IpcContract.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(ct); }
            catch (OperationCanceledException) { break; }
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(ct);
                if (line is null) continue;
                var req = JsonSerializer.Deserialize<IpcRequest>(line, IpcContract.Json);
                var resp = req is null
                    ? new IpcResponse(false, "bad request", null)
                    : IpcDispatcher.Handle(req, _svc());
                await writer.WriteLineAsync(JsonSerializer.Serialize(resp, IpcContract.Json));
            }
            catch (IOException) { /* client vanished; loop */ }
        }
    }
}
