using System.Diagnostics;
using System.IO;

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

    /// <summary>Open a terminal in <paramref name="dir"/> — Windows Terminal, then PowerShell, then cmd.</summary>
    public static bool Terminal(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        var attempts = new (string file, string args)[]
        {
            ("wt.exe", $"-d \"{dir}\""),
            ("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{dir}'\""),
            ("cmd.exe", $"/k cd /d \"{dir}\""),
        };
        foreach (var (file, args) in attempts)
        {
            try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); return true; }
            catch { /* try the next shell */ }
        }
        return false;
    }
}
