namespace Dzl.Core.Env;

public static class SteamInstall
{
    public const int DayZ = 221100;
    public const int DayZTools = 830640;
    public const int DayZServer = 223350;

    public static string InstallUri(int appId) => $"steam://install/{appId}";
    public static string ValidateUri(int appId) => $"steam://validate/{appId}";

    // Opens the Steam client's install dialog for the app. Returns false if it couldn't launch.
    // Manual / not unit-tested (shell-exec).
    public static bool Install(int appId) => Launch(InstallUri(appId));

    // Triggers Steam's "verify integrity of game files" for the app (fixes a corrupted install).
    public static bool Validate(int appId) => Launch(ValidateUri(appId));

    private static bool Launch(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
