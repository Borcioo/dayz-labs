using System.Diagnostics;

namespace Dzl.Core.Workshop;

/// <summary>Drives steamcmd for Workshop downloads. Arg/path building is pure + unit-tested; the run spawns
/// a visible console so the user can complete Steam login / Steam Guard (owned DayZ items need ownership).</summary>
public static class WorkshopCmd
{
    public const string AppId = "221100";

    /// <summary>steamcmd command tail: <c>+login &lt;user|anonymous&gt; +workshop_download_item 221100 &lt;id&gt; +quit</c>.</summary>
    public static string CommandLine(string? login, string id) =>
        $"+login {(string.IsNullOrWhiteSpace(login) ? "anonymous" : login!.Trim())} "
        + $"+workshop_download_item {AppId} {id} +quit";

    /// <summary>Where steamcmd places a downloaded item: <c>&lt;steamcmd dir&gt;\steamapps\workshop\content\221100\&lt;id&gt;</c>.</summary>
    public static string ContentDir(string steamCmdExe, string id) =>
        Path.Combine(Path.GetDirectoryName(steamCmdExe) ?? ".", "steamapps", "workshop", "content", AppId, id);

    /// <summary>Spawn steamcmd in a console to download/update an item. Returns false if the exe is missing or
    /// launch fails. The console stays open (<c>cmd /k</c>) so login/Guard prompts and progress are visible.</summary>
    public static bool Download(string steamCmdExe, string? login, string id)
    {
        if (string.IsNullOrWhiteSpace(steamCmdExe) || !File.Exists(steamCmdExe) || string.IsNullOrWhiteSpace(id))
            return false;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/k \"\"{steamCmdExe}\" {CommandLine(login, id)}\"")
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
