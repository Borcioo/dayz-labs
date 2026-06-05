using System.Security.Cryptography;
using System.Text;

namespace Dzl.Core.Workshop;

/// <summary>Stores the Steam <b>refresh token</b> (long-lived, account-sensitive) encrypted at rest with
/// Windows DPAPI (CurrentUser scope) in <c>&lt;configDir&gt;\steam.token</c> — never in config.json. The
/// short-lived access token is kept only in memory and renewed from this. Never throws.</summary>
public static class SteamTokenStore
{
    private static string TokenFile(string configPath) =>
        Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "steam.token");

    public static void Save(string configPath, string refreshToken)
    {
        if (!OperatingSystem.IsWindows()) return;   // DPAPI is Windows-only (dzl is a Windows app)
        try
        {
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile(configPath), enc);
        }
        catch { /* best-effort; sign-in can be repeated */ }
    }

    public static string? Load(string configPath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var f = TokenFile(configPath);
            if (!File.Exists(f)) return null;
            var dec = ProtectedData.Unprotect(File.ReadAllBytes(f), null, DataProtectionScope.CurrentUser);
            var s = Encoding.UTF8.GetString(dec);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch { return null; }
    }

    public static bool Exists(string configPath) => File.Exists(TokenFile(configPath));

    public static void Clear(string configPath)
    {
        try { var f = TokenFile(configPath); if (File.Exists(f)) File.Delete(f); } catch { /* best-effort */ }
    }
}
