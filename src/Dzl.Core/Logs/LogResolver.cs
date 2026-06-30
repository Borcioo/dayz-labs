namespace Dzl.Core.Logs;

public static class LogResolver
{
    private static string? Newest(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, pattern)
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    public static Dictionary<string, string?> Resolve(string serverProfiles, string clientProfiles) => new()
    {
        ["script"]  = Newest(serverProfiles, "script_*.log"),
        ["rpt"]     = Newest(serverProfiles, "*.RPT"),
        ["adm"]     = Newest(serverProfiles, "*.ADM"),
        ["console"] = Newest(serverProfiles, "server_console.log"),
        ["client"]  = Newest(clientProfiles, "script_*.log"),
    };
}
