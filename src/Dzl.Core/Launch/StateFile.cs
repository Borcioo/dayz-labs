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
        try { return JsonSerializer.Deserialize<Dictionary<string, ProcInfo>>(File.ReadAllText(p), Json) ?? new(); }
        catch (JsonException) { return new(); }
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
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(configPath))!);
        File.WriteAllText(Path(configPath), JsonSerializer.Serialize(data, Json));
    }
}
