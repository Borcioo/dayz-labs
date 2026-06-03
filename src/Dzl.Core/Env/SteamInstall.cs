namespace Dzl.Core.Env;

public static class SteamInstall
{
    public const int DayZ = 221100;
    public const int DayZTools = 830640;
    public const int DayZServer = 223350;

    public static string InstallUri(int appId) => $"steam://install/{appId}";
    public static string ValidateUri(int appId) => $"steam://validate/{appId}";
    public static string RunUri(int appId) => $"steam://run/{appId}";

    // Opens the Steam client's install dialog for the app. Returns false if it couldn't launch.
    // Manual / not unit-tested (shell-exec).
    public static bool Install(int appId) => Launch(InstallUri(appId));

    // Triggers Steam's "verify integrity of game files" for the app (fixes a corrupted install).
    public static bool Validate(int appId) => Launch(ValidateUri(appId));

    // Launches the app THROUGH Steam (correct working dir, registry/install-script applied) —
    // the same way clicking it in the Steam library does. Use this instead of starting the
    // exe directly (DayZToolsLauncher resolves settings.ini relative to its cwd and crashes otherwise).
    public static bool Run(int appId) => Launch(RunUri(appId));

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
