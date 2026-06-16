using System.IO;

namespace Dzl.Tray;

/// <summary>
/// Creates / removes a Start-menu "Uninstall dzl" shortcut that runs Velopack's
/// <c>Update.exe uninstall</c>. Velopack already registers the app in Apps &amp; features and drops
/// a launch shortcut; this just adds a discoverable Start-menu entry so users who look there for an
/// uninstaller find one. Driven by the install / uninstall hooks (best-effort; callers swallow).
/// </summary>
public static class UninstallShortcut
{
    private const string LinkName = "Uninstall dzl.lnk";

    private static string LinkPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LinkName);

    public static void Create()
    {
        // During the install hook AppContext.BaseDirectory is %LocalAppData%\DayZLabs\current\;
        // Update.exe (the uninstaller) lives one level up in the install root.
        var updateExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));
        if (!File.Exists(updateExe)) return;

        // No managed API creates a .lnk — use the Windows Script Host COM object.
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(LinkPath);
        link.TargetPath = updateExe;
        link.Arguments = "uninstall";
        link.WorkingDirectory = Path.GetDirectoryName(updateExe);
        link.Description = "Uninstall dzl";
        link.Save();
    }

    public static void Remove()
    {
        if (File.Exists(LinkPath)) File.Delete(LinkPath);
    }
}
