using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dzl.Core.Env;

public sealed record DetectedPaths(string? DayzPath, string? ToolsPath, string? ServerPath);

public static class EnvDetect
{
    // Matches    "path"   "<value>"   entries in libraryfolders.vdf, robust to tabs/spaces.
    private static readonly Regex PathEntry =
        new("\"path\"\\s*\"([^\"]*)\"", RegexOptions.Compiled);

    // Matches a   WorkDirPath = "<value>"   line in DayZ Tools' settings.ini (quotes/=/ws optional).
    private static readonly Regex WorkDirEntry =
        new(@"^\s*WorkDirPath\s*=?\s*""?([^""\r\n]*?)""?\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>Extract every library root path from a libraryfolders.vdf, unescaping \\ -> \, in order.</summary>
    public static List<string> ParseLibraryFolders(string vdfText)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(vdfText)) return result;
        foreach (Match m in PathEntry.Matches(vdfText))
            result.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        return result;
    }

    /// <summary>
    /// Read the <c>WorkDirPath</c> value out of a DayZ Tools <c>settings.ini</c>
    /// (e.g. <c>WorkDirPath="C:\Users\m\DayZ Projects"</c>). Quotes and surrounding
    /// whitespace are stripped. Returns null if absent.
    /// </summary>
    public static string? ParseWorkDir(string settingsIniText)
    {
        if (string.IsNullOrEmpty(settingsIniText)) return null;
        var m = WorkDirEntry.Match(settingsIniText);
        if (!m.Success) return null;
        var v = m.Groups[1].Value.Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>Read <c>&lt;toolsPath&gt;\settings.ini</c> and return its WorkDirPath; null if missing. Thin/manual.</summary>
    public static string? WorkDir(string toolsPath)
    {
        if (string.IsNullOrWhiteSpace(toolsPath)) return null;
        try
        {
            var ini = Path.Combine(toolsPath, "settings.ini");
            return File.Exists(ini) ? ParseWorkDir(File.ReadAllText(ini)) : null;
        }
        catch
        {
            return null;
        }
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
