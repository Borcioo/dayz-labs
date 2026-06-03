namespace Dzl.Core.Env;

public static class SteamInstall
{
    public const int DayZ = 221100;
    public const int DayZTools = 830640;
    public const int DayZServer = 223350;

    public static string InstallUri(int appId) => $"steam://install/{appId}";

    // Opens the Steam client's install dialog for the app. Returns false if it couldn't launch.
    // Manual / not unit-tested (shell-exec).
    public static bool Install(int appId)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(InstallUri(appId)) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
