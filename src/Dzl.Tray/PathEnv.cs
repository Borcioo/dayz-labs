namespace Dzl.Tray;

/// <summary>
/// Adds/removes a directory on the current user's PATH. The string math is pure and tested;
/// the I/O wrappers persist via the User environment target (which on Windows writes
/// HKCU\Environment and broadcasts WM_SETTINGCHANGE so new processes see the change).
/// Used by the Velopack install/uninstall hooks to expose the bundled CLI (dzl.exe).
/// </summary>
public static class PathEnv
{
    /// <summary>Returns <paramref name="current"/> with <paramref name="dir"/> appended, or unchanged
    /// if already present (case-insensitive, trailing backslash ignored).</summary>
    public static string EnsurePresent(string? current, string dir)
    {
        var target = dir.TrimEnd('\\');
        var parts = (current ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
            if (string.Equals(p.Trim().TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase))
                return current ?? "";
        var prefix = string.IsNullOrEmpty(current) ? "" : current.TrimEnd(';') + ";";
        return prefix + dir;
    }

    /// <summary>Returns <paramref name="current"/> with every entry equal to <paramref name="dir"/>
    /// removed (case-insensitive, trailing backslash ignored).</summary>
    public static string Remove(string? current, string dir)
    {
        var target = dir.TrimEnd('\\');
        var kept = (current ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.Equals(p.Trim().TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase));
        return string.Join(';', kept);
    }

    public static void AddDirToUserPath(string dir)
    {
        var cur = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var next = EnsurePresent(cur, dir);
        if (next != (cur ?? "")) Environment.SetEnvironmentVariable("PATH", next, EnvironmentVariableTarget.User);
    }

    public static void RemoveDirFromUserPath(string dir)
    {
        var cur = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var next = Remove(cur, dir);
        if (next != (cur ?? "")) Environment.SetEnvironmentVariable("PATH", next, EnvironmentVariableTarget.User);
    }
}
