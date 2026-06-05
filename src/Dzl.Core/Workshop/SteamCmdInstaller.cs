using System.IO.Compression;

namespace Dzl.Core.Workshop;

/// <summary>Downloads + extracts Valve's official steamcmd into a folder (so the user doesn't have to fetch
/// it manually). The extracted steamcmd.exe self-bootstraps the rest on first run. Never throws.</summary>
public static class SteamCmdInstaller
{
    public const string DownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>Install steamcmd into <paramref name="destDir"/>. Returns (ok, exePath, message).</summary>
    public static async Task<(bool ok, string exePath, string message)> InstallAsync(string destDir)
    {
        var exe = Path.Combine(destDir, "steamcmd.exe");
        try
        {
            Directory.CreateDirectory(destDir);
            var bytes = await Http.GetByteArrayAsync(DownloadUrl).ConfigureAwait(false);
            var tmpZip = Path.Combine(Path.GetTempPath(), $"dzl-steamcmd-{Guid.NewGuid():N}.zip");
            await File.WriteAllBytesAsync(tmpZip, bytes).ConfigureAwait(false);
            try { ZipFile.ExtractToDirectory(tmpZip, destDir, overwriteFiles: true); }
            finally { try { File.Delete(tmpZip); } catch { /* temp cleanup best-effort */ } }

            return File.Exists(exe)
                ? (true, exe, "steamcmd installed — its first run will self-update")
                : (false, exe, "downloaded + extracted, but steamcmd.exe wasn't found");
        }
        catch (Exception ex)
        {
            return (false, exe, ex.Message);
        }
    }
}
