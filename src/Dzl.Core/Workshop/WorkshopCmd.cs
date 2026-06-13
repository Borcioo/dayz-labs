using System.Diagnostics;

namespace Dzl.Core.Workshop;

/// <summary>Drives steamcmd for Workshop downloads. Path/arg building is pure + unit-tested; the run
/// spawns a visible console so login / Steam Guard prompts are visible.</summary>
/// <remarks>steamcmd always nests items under <c>steamapps\workshop\content\221100\&lt;id&gt;</c>, so it
/// downloads into a hidden <c>.steamcmd</c> cache and a post-step junctions <c>&lt;installRoot&gt;\&lt;id&gt;</c>
/// to that content — the user sees a clean path, the cache keeps steamcmd's manifests for incremental
/// updates, and a junction needs no copy and no elevation.</remarks>
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

    /// <summary>The hidden cache where steamcmd does its <c>steamapps\workshop\content</c> nesting.</summary>
    public static string CacheDir(string installRoot) => Path.Combine(installRoot, ".steamcmd");

    /// <summary>steamcmd's raw nested location for an item, inside the cache.</summary>
    public static string RawDir(string installRoot, string id) =>
        Path.Combine(CacheDir(installRoot), "steamapps", "workshop", "content", AppId, id);

    /// <summary>The clean, user-facing location for an item: <c>&lt;installRoot&gt;\&lt;id&gt;</c> (a junction to
    /// <see cref="RawDir"/> after a successful download).</summary>
    public static string ContentDir(string installRoot, string id) => Path.Combine(installRoot, id);

    /// <summary>Spawn steamcmd in a console to download an item into <paramref name="installRoot"/> as a clean
    /// <c>&lt;installRoot&gt;\&lt;id&gt;</c> path. Returns false if the exe is missing or launch fails. The console
    /// stays open (<c>cmd /k</c>) so login/Guard prompts and progress are visible; once steamcmd quits the batch
    /// junctions the clean path to the downloaded content.</summary>
    public static bool Download(string steamCmdExe, string? login, string id, string installRoot)
    {
        if (string.IsNullOrWhiteSpace(steamCmdExe) || !File.Exists(steamCmdExe) || string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(installRoot))
            return false;
        try
        {
            var cache = CacheDir(installRoot);
            Directory.CreateDirectory(cache);
            var raw = RawDir(installRoot, id);
            var dest = ContentDir(installRoot, id);
            var bat = Path.Combine(cache, $"dl_{id}.bat");
            // steamcmd into the cache, then expose it at the clean path via a junction (mklink /J — no admin).
            var script =
                $"""
                @echo off
                "{steamCmdExe}" {CommandLine(login, id, cache)}
                if exist "{raw}" (
                  if exist "{dest}" rmdir "{dest}" 2>nul
                  mklink /J "{dest}" "{raw}" >nul
                  echo.
                  echo Downloaded to: {dest}
                ) else (
                  echo.
                  echo Download did not complete — see the steamcmd output above.
                )
                """;
            File.WriteAllText(bat, script);
            var psi = new ProcessStartInfo("cmd.exe", $"/k \"{bat}\"")
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
