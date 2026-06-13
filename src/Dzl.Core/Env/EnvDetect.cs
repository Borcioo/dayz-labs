using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dzl.Core.Env;

public sealed record DetectedPaths(string? DayzPath, string? ToolsPath, string? ServerPath);

public static class EnvDetect
{
    /// <summary>True if THIS process runs elevated (Administrator). Windows-only.</summary>
    /// <remarks>Matters because mapped/subst drives (like the P: work drive) are per-session AND
    /// per-elevation: an admin process sees drives the normal user session (Explorer, the game) does
    /// not, and vice versa.</remarks>
    public static bool IsElevated()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private const string LinkedConnKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    /// <summary>True if Windows' EnableLinkedConnections policy is on (mapped/subst drives are
    /// shared between a user's elevated and normal sessions — fixes "admin sees P:, Explorer doesn't").</summary>
    public static bool LinkedConnectionsEnabled()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var k = Registry.LocalMachine.OpenSubKey(LinkedConnKey);
            return (k?.GetValue("EnableLinkedConnections") as int?) == 1;
        }
        catch { return false; }
    }

    /// <summary>Set EnableLinkedConnections=1 (HKLM — needs admin; dzl has it when elevated). A reboot
    /// (or restart of LanmanWorkstation) is required to take effect. Returns true on success.</summary>
    public static bool TryEnableLinkedConnections()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var k = Registry.LocalMachine.CreateSubKey(LinkedConnKey, writable: true);
            k!.SetValue("EnableLinkedConnections", 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // Matches    "path"   "<value>"   entries in libraryfolders.vdf, robust to tabs/spaces.
    private static readonly Regex PathEntry =
        new("\"path\"\\s*\"([^\"]*)\"", RegexOptions.Compiled);

    // DayZ Tools settings.ini real format: a [ProjectDrive] section with `path=<dir>`
    // (the work-drive source folder), and a [Game] section with `path=<dir>`.
    private static readonly Regex ProjectDrivePath =
        new(@"\[ProjectDrive\][^\[]*?\bpath\s*=\s*([^\r\n]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex GamePathEntry =
        new(@"\[Game\][^\[]*?\bpath\s*=\s*([^\r\n]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    // Legacy fallback: a   WorkDirPath = "<value>"   line (WorkDrive.exe's runtime dump form).
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

    /// <summary>Read the work-drive source folder out of a DayZ Tools <c>settings.ini</c> (the
    /// <c>[ProjectDrive] path=</c> value, with a legacy <c>WorkDirPath=</c> fallback). Quotes and
    /// surrounding whitespace are stripped; null if absent.</summary>
    public static string? ParseWorkDir(string settingsIniText)
    {
        if (string.IsNullOrEmpty(settingsIniText)) return null;
        // Prefer the real [ProjectDrive] path=, fall back to a legacy WorkDirPath= line.
        var v = ProjectDrivePath.Match(settingsIniText) is { Success: true } p ? p.Groups[1].Value.Trim()
              : WorkDirEntry.Match(settingsIniText) is { Success: true } w ? w.Groups[1].Value.Trim()
              : "";
        return v.Length == 0 ? null : v;
    }

    /// <summary>Read the [Game] path= value out of a DayZ Tools settings.ini; null if absent.</summary>
    public static string? ParseGamePath(string settingsIniText)
    {
        if (string.IsNullOrEmpty(settingsIniText)) return null;
        var m = GamePathEntry.Match(settingsIniText);
        if (!m.Success) return null;
        var v = m.Groups[1].Value.Trim();
        return v.Length == 0 ? null : v;
    }

    /// <summary>Effective work-drive source folder: the explicit override if non-empty, else the value
    /// derived from DayZ Tools settings.ini (<see cref="WorkDir"/>). Null when neither is available.</summary>
    public static string? WorkDriveSource(string? overrideFolder, string toolsPath) =>
        !string.IsNullOrWhiteSpace(overrideFolder) ? overrideFolder!.Trim() : WorkDir(toolsPath);

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
            // Steam's libraryfolders.vdf can yield forward-slash roots; normalize so the
            // path uses consistent backslashes (a mixed path also crashes OpenFolderDialog).
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    /// <summary>True if DayZ Tools' registry config exists (HKCU\SOFTWARE\Bohemia Interactive\Dayz Tools
    /// with a non-empty <c>path</c>), i.e. Steam's install script ran. Windows-only.</summary>
    /// <remarks>WorkDrive needs this; when it's missing WorkDrive reports "install corrupted".</remarks>
    public static bool ToolsRegistered()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Bohemia Interactive\Dayz Tools");
            return !string.IsNullOrWhiteSpace(k?.GetValue("path") as string);
        }
        catch { return false; }
    }

    /// <summary>Best-effort DayZ build/version string from the install's exe (FileVersion/ProductVersion),
    /// for tagging a base/template. "unknown" if it can't be read.</summary>
    public static string DayzVersion(string dayzPath)
    {
        foreach (var exe in new[] { "DayZServer_x64.exe", "DayZ_x64.exe", "DayZDiag_x64.exe" })
        {
            try
            {
                var p = Path.Combine(dayzPath, exe);
                if (!File.Exists(p)) continue;
                var fi = FileVersionInfo.GetVersionInfo(p);
                var v = fi.ProductVersion ?? fi.FileVersion;
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            catch { /* try next */ }
        }
        return "unknown";
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
