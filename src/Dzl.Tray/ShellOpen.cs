using System.Diagnostics;

namespace Dzl.Tray;

/// <summary>Open a folder in Explorer reliably. Passing the folder as the shell target (the
/// <see cref="ProcessStartInfo.FileName"/>) — rather than as an <c>explorer.exe &lt;path&gt;</c> argument —
/// means an empty, missing, or broken-junction path throws (and is swallowed here) instead of explorer
/// silently falling back to the user's Documents folder.</summary>
internal static class ShellOpen
{
    public static bool Folder(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir.Trim(), UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
