using System.Diagnostics;

namespace Dzl.Core.Tools;

public static class WorkDrive
{
    // P: is the conventional DayZ work drive. Mounted iff the path exists.
    public static bool IsMounted(string drive = @"P:\") => Directory.Exists(drive);

    // Mount via DayZ Tools' WorkDrive.exe if present, else `subst P: <path>`.
    public static bool Mount(string workDriveExeOrSourcePath, string drive = "P:")
    {
        if (IsMounted(drive + "\\")) return true;
        ProcessStartInfo psi;
        if (File.Exists(workDriveExeOrSourcePath) &&
            workDriveExeOrSourcePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            psi = new ProcessStartInfo(workDriveExeOrSourcePath) { UseShellExecute = true };
        else
            psi = new ProcessStartInfo("subst", $"{drive} \"{workDriveExeOrSourcePath}\"") { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return IsMounted(drive + "\\");
    }

    public static void Unmount(string drive = "P:")
    {
        using var p = Process.Start(new ProcessStartInfo("subst", $"{drive} /D") { UseShellExecute = false, CreateNoWindow = true });
        p?.WaitForExit();
    }
}
