using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dzl.Core.Env;

public sealed record DetectedPaths(string? DayzPath, string? ToolsPath, string? ServerPath);

public static class EnvDetect
{
    // Matches    "path"   "<value>"   entries in libraryfolders.vdf, robust to tabs/spaces.
    private static readonly Regex PathEntry =
        new("\"path\"\\s*\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>Extract every library root path from a libraryfolders.vdf, unescaping \\ -> \, in order.</summary>
    public static List<string> ParseLibraryFolders(string vdfText)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(vdfText)) return result;
        foreach (Match m in PathEntry.Matches(vdfText))
            result.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        return result;
    }

    /// <summary>First &lt;lib&gt;\steamapps\common\&lt;relFolder&gt; that exists, else null.</summary>
    public static string? FindApp(IEnumerable<string> libraries, string relFolder)
    {
        foreach (var lib in libraries)
        {
            var candidate = Path.Combine(lib, "steamapps", "common", relFolder);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Steam install path from registry (Windows-only); null if not found.</summary>
    public static string? SteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var hkcu = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(hkcu)) return hkcu;
        var hklm = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return string.IsNullOrEmpty(hklm) ? null : hklm;
    }

    /// <summary>Compose Steam + library detection into DayZ / Tools / Server paths. Thin/manual.</summary>
    public static DetectedPaths Detect()
    {
        var steam = SteamPath();
        if (string.IsNullOrEmpty(steam)) return new DetectedPaths(null, null, null);

        var libs = new List<string> { steam };
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                libs.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        }
        catch
        {
            // unreadable/locked vdf -> treat as empty; main library still searched
        }

        return new DetectedPaths(
            FindApp(libs, "DayZ"),
            FindApp(libs, "DayZ Tools"),
            FindApp(libs, "DayZServer"));
    }
}
