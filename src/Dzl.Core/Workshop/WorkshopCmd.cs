using System.Diagnostics;

namespace Dzl.Core.Workshop;

/// <summary>Drives steamcmd for Workshop downloads. Arg/path building is pure + unit-tested; the run spawns
/// a visible console so the user can complete Steam login / Steam Guard (owned DayZ items need ownership).</summary>
public static class WorkshopCmd
{
    public const string AppId = "221100";

    /// <summary>steamcmd command tail. <c>+force_install_dir</c> (when an <paramref name="installDir"/> is given)
    /// MUST come before <c>+login</c> or steamcmd ignores it: <c>+force_install_dir "&lt;dir&gt;" +login
    /// &lt;user|anonymous&gt; +workshop_download_item 221100 &lt;id&gt; +quit</c>.</summary>
    public static string CommandLine(string? login, string id, string? installDir = null) =>
        (string.IsNullOrWhiteSpace(installDir) ? "" : $"+force_install_dir \"{installDir}\" ")
        + $"+login {(string.IsNullOrWhiteSpace(login) ? "anonymous" : login!.Trim())} "
        + $"+workshop_download_item {AppId} {id} +quit";

    /// <summary>Where steamcmd places a downloaded item: <c>&lt;installRoot&gt;\steamapps\workshop\content\221100\&lt;id&gt;</c>,
    /// where <paramref name="installRoot"/> is the <c>+force_install_dir</c> (or steamcmd's own dir when none).</summary>
    public static string ContentDir(string installRoot, string id) =>
        Path.Combine(installRoot, "steamapps", "workshop", "content", AppId, id);

    /// <summary>Spawn steamcmd in a console to download/update an item into <paramref name="installDir"/> (its
    /// <c>+force_install_dir</c>; null = next to the exe). Returns false if the exe is missing or launch fails.
    /// The console stays open (<c>cmd /k</c>) so login/Guard prompts and progress are visible.</summary>
    public static bool Download(string steamCmdExe, string? login, string id, string? installDir = null)
    {
        if (string.IsNullOrWhiteSpace(steamCmdExe) || !File.Exists(steamCmdExe) || string.IsNullOrWhiteSpace(id))
            return false;
        try
        {
            if (!string.IsNullOrWhiteSpace(installDir)) Directory.CreateDirectory(installDir);
            var psi = new ProcessStartInfo("cmd.exe", $"/k \"\"{steamCmdExe}\" {CommandLine(login, id, installDir)}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(steamCmdExe) ?? Environment.CurrentDirectory,
            };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
