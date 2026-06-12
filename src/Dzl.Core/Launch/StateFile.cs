using System.Text.Json;

namespace Dzl.Core.Launch;

public sealed record ProcInfo(int Pid, string Mode, string Source, string Exe);

public static class StateFile
{
    private static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    public static string Path(string configPath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(configPath) ?? ".", ".dzl-procs.json");

    public static Dictionary<string, ProcInfo> ReadRaw(string configPath)
    {
        var p = Path(configPath);
        if (!File.Exists(p)) return new();
        // IOException too: the tray polls this file every 1.5 s while the CLI/MCP write it —
        // a sharing violation mid-write must read as "no entries", not escape through Status().
        try { return JsonSerializer.Deserialize<Dictionary<string, ProcInfo>>(File.ReadAllText(p), Json) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { return new(); }
    }

    // imageOf(pid) -> image name or null if not running (injectable for tests).
    public static Dictionary<string, ProcInfo> ReadLive(string configPath, Func<int, string?> imageOf)
    {
        var raw = ReadRaw(configPath);
        var live = raw.Where(kv =>
        {
            var img = imageOf(kv.Value.Pid);
            return img is not null && string.Equals(img, kv.Value.Exe, StringComparison.OrdinalIgnoreCase);
        }).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (live.Count != raw.Count) WriteAll(configPath, live);
        return live;
    }

    public static void Write(string configPath, string target, int pid, string mode, string source, string exe)
    {
        var data = ReadRaw(configPath);
        data[target] = new ProcInfo(pid, mode, source, exe);
        WriteAll(configPath, data);
    }

    public static void Clear(string configPath, string target)
    {
        var data = ReadRaw(configPath);
        if (data.Remove(target)) WriteAll(configPath, data);
    }

    private static void WriteAll(string configPath, Dictionary<string, ProcInfo> data)
    {
        var path = Path(configPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        // Atomic swap so a concurrent reader (tray poll vs CLI/MCP) never sees a half-written file.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
