using System.Security.Cryptography;
using System.Text;

namespace Dzl.Core.Workshop;

/// <summary>Stores the Steam tokens encrypted at rest with Windows DPAPI (CurrentUser scope) next to the
/// config — never in config.json. Never throws.</summary>
/// <remarks>The <b>refresh token</b> (long-lived) lives in <c>steam.token</c>; the shorter-lived
/// <b>access token</b> (what the Workshop API needs, ~24h) is cached in <c>steam.access</c> so a restart
/// can reuse it without re-minting (token renewal from the refresh token is unreliable).</remarks>
public static class SteamTokenStore
{
    private static string Dir(string configPath) => Path.GetDirectoryName(configPath) ?? ".";
    private static string RefreshFile(string configPath) => Path.Combine(Dir(configPath), "steam.token");
    private static string AccessFile(string configPath) => Path.Combine(Dir(configPath), "steam.access");

    private static void Write(string file, string value)
    {
        if (!OperatingSystem.IsWindows()) return;   // DPAPI is Windows-only (dzl is a Windows app)
        try
        {
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(file, enc);
        }
        catch { /* best-effort; sign-in can be repeated */ }
    }

    private static string? Read(string file)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            if (!File.Exists(file)) return null;
            var dec = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            var s = Encoding.UTF8.GetString(dec);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch { return null; }
    }

    public static void Save(string configPath, string refreshToken) => Write(RefreshFile(configPath), refreshToken);
    public static string? Load(string configPath) => Read(RefreshFile(configPath));

    public static void SaveAccess(string configPath, string accessToken) => Write(AccessFile(configPath), accessToken);
    public static string? LoadAccess(string configPath) => Read(AccessFile(configPath));

    public static bool Exists(string configPath) => File.Exists(RefreshFile(configPath));

    public static void Clear(string configPath)
    {
        foreach (var f in new[] { RefreshFile(configPath), AccessFile(configPath) })
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best-effort */ }
    }
}
