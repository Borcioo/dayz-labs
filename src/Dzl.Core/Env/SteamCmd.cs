namespace Dzl.Core.Env;

public static class SteamCmd
{
    /// <summary>
    /// steamcmd one-liner that installs/validates the DayZ dedicated server (app 223350) into serverDir.
    /// Pure string; the caller supplies their Steam login.
    /// </summary>
    public static string DownloadServerScript(string serverDir, string steamUser = "YOUR_STEAM_LOGIN") =>
        $"steamcmd +force_install_dir \"{serverDir}\" +login {steamUser} +app_update 223350 validate +quit";
}
