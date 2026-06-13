using Dzl.Core.Tools;

namespace Dzl.Core.Launch;

/// <summary>Finds + launches the tray app as a monitor when a CLI/MCP start happens with no tray up.
/// <see cref="Resolve"/> is pure; mutex check + launch are thin/manual.</summary>
public static class TrayLauncher
{
    /// <summary>Locate Dzl.Tray.exe next to <paramref name="baseDir"/> (the running CLI/MCP exe dir), or null.</summary>
    public static string? Resolve(string baseDir)
    {
        try
        {
            var p = Path.Combine(baseDir, "Dzl.Tray.exe");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>True if the single-instance tray mutex exists (tray already running). Windows-only.</summary>
    public static bool IsTrayRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting("dzl-tray-singleton", out var m)) return false;
            m?.Dispose();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch { return false; }
    }

    /// <summary>Launch the tray hidden (--tray), de-elevated, as a monitor. Best-effort; never throws.</summary>
    /// <remarks>Uses <see cref="ProcessElevation.Run"/> with <c>timeoutMs = 0</c>: both the normal and
    /// de-elevated code paths call WaitForExit/WaitForSingleObject with 0 ms, which returns immediately
    /// without waiting for the child to exit. The tray process keeps running detached — this call does
    /// NOT block the CLI/MCP caller.</remarks>
    public static void LaunchMonitor(string baseDir)
    {
        var exe = Resolve(baseDir);
        if (exe is null) return;
        try
        {
            ProcessElevation.Run(exe, new List<string> { "--tray" },
                Path.GetDirectoryName(exe) ?? "", timeoutMs: 0, deElevateIfAdmin: true, showWindow: false);
        }
        catch { /* best-effort monitor */ }
    }
}
