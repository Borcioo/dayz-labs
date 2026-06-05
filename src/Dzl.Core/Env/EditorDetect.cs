namespace Dzl.Core.Env;

/// <summary>A detected code editor: friendly <see cref="Name"/> + the launcher <see cref="Path"/> (exe or
/// PATH cli) that opens a folder via <c>&lt;path&gt; &lt;folder&gt;</c>.</summary>
public sealed record EditorInfo(string Name, string Path);

/// <summary>Finds installed code editors (Cursor, VS Code, …) for the "Open in editor" actions. Detection is
/// PATH-first (most reliable — editors register a CLI shim like <c>cursor</c>/<c>code</c>), with known
/// install-location fallbacks. The PATH search is pure + unit-tested.</summary>
public static class EditorDetect
{
    // Friendly name, PATH command, in preference order (Cursor first — user's editor of choice).
    private static readonly (string Name, string Cmd)[] Known =
    {
        ("Cursor", "cursor"),
        ("VS Code", "code"),
        ("VS Code Insiders", "code-insiders"),
        ("Sublime Text", "subl"),
        ("Rider", "rider"),
        ("IntelliJ IDEA", "idea"),
    };

    /// <summary>First existing <c>&lt;dir&gt;\&lt;cmd&gt;{.cmd|.exe|.bat|.com|}</c> across the PATH dirs, or null.
    /// Pure (PATH passed in) so it can be unit-tested.</summary>
    public static string? FindOnPath(string cmd, string pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue)) return null;
        var exts = new[] { ".cmd", ".exe", ".bat", ".com", "" };
        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var d = dir.Trim().Trim('"');
            if (d.Length == 0) continue;
            foreach (var ext in exts)
            {
                try { var full = Path.Combine(d, cmd + ext); if (File.Exists(full)) return full; }
                catch { /* malformed PATH entry — skip */ }
            }
        }
        return null;
    }

    /// <summary>Detected editors (deduped, in preference order). Empty if none found.</summary>
    public static List<EditorInfo> Detect()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        // Known exe fallbacks when the CLI shim isn't on PATH.
        var fallbacks = new Dictionary<string, string[]>
        {
            ["cursor"] = new[] { Path.Combine(pf, "cursor", "Cursor.exe"), Path.Combine(local, "Programs", "cursor", "Cursor.exe") },
            ["code"] = new[] { Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"), Path.Combine(pf, "Microsoft VS Code", "Code.exe") },
            ["code-insiders"] = new[] { Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe") },
        };

        var found = new List<EditorInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, cmd) in Known)
        {
            var hit = FindOnPath(cmd, path);
            if (hit is null && fallbacks.TryGetValue(cmd, out var fbs))
                hit = fbs.FirstOrDefault(File.Exists);
            if (hit is not null && seen.Add(hit))
                found.Add(new EditorInfo(name, hit));
        }
        return found;
    }
}
