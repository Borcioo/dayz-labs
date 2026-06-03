namespace Dzl.Core.Logs;

public static class LogTail
{
    public static List<string> LastLines(string path, int n)
    {
        if (!File.Exists(path)) return new();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var buf = new LinkedList<string>();
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            buf.AddLast(line);
            if (buf.Count > n) buf.RemoveFirst();
        }
        return buf.ToList();
    }

    // Best-effort follow: emit new lines as they're appended. Manual-verify only.
    public static async Task Follow(string path, Action<string> onLine, CancellationToken ct)
    {
        long pos = File.Exists(path) ? new FileInfo(path).Length : 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length < pos) pos = 0; // file rotated/truncated
                    fs.Seek(pos, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = await sr.ReadLineAsync()) is not null)
                    {
                        ct.ThrowIfCancellationRequested();
                        onLine(line);
                    }
                    pos = fs.Length;
                }
            }
            catch (IOException) { /* transient; retry next tick */ }

            try
            {
                await Task.Delay(500, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
