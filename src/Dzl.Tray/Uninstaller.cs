using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Dzl.Tray;

/// <summary>
/// Drives a clean in-app uninstall. The running tray locks files in its own install dir, so a
/// direct <c>Update.exe uninstall</c> while we're alive returns success but leaves the dir behind
/// (verified). Instead we spawn a detached PowerShell that waits for THIS process to exit, then
/// (optionally) wipes the config dir, then runs the Velopack uninstaller — which removes the app,
/// the PATH entry, and the shortcuts once nothing is locked.
/// </summary>
public static class Uninstaller
{
    // Update.exe lives in the install root, one level up from the running app's "current" dir.
    private static string UpdateExe =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));

    /// <summary>True only for an installed Velopack build (Update.exe present); false in a dev run.</summary>
    public static bool CanUninstall => File.Exists(UpdateExe);

    /// <summary>Hands off to a detached uninstaller and shuts the app down so it can finish.
    /// When <paramref name="removeUserData"/> is true the config dir (%LocalAppData%\dzl) is wiped too.</summary>
    public static void Run(bool removeUserData)
    {
        if (!CanUninstall) return;

        var ps = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; ";
        if (removeUserData)
        {
            var dir = Path.GetDirectoryName(App.ConfigPath());
            if (!string.IsNullOrEmpty(dir))
                ps += $"Remove-Item -LiteralPath '{Esc(dir)}' -Recurse -Force -ErrorAction SilentlyContinue; ";
        }
        ps += $"& '{Esc(UpdateExe)}' uninstall -s";

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(ps);
        Process.Start(psi);

        Application.Current.Shutdown();
    }

    // Escape single quotes for embedding a path inside a single-quoted PowerShell string.
    private static string Esc(string s) => s.Replace("'", "''");
}
