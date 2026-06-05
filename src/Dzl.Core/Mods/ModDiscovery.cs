using Dzl.Core.Config;

namespace Dzl.Core.Mods;

public sealed record Mod(string Name, string Path, bool Enabled, string Side, bool Missing);

public static class ModDiscovery
{
    public static bool IsMod(string dir) =>
        Directory.Exists(System.IO.Path.Combine(dir, "addons"))
        || (File.Exists(System.IO.Path.Combine(dir, "config.cpp"))
            && Directory.Exists(System.IO.Path.Combine(dir, "scripts")));

    // All immediate subdirectories of each root that look like a mod. A root that is missing, offline, a
    // dangling junction, or inaccessible is skipped — never throws (a bad scan root must not crash callers).
    public static List<string> Discover(IEnumerable<string> roots)
    {
        var found = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                    if (IsMod(dir)) found.Add(dir);
            }
            catch (IOException) { /* missing/dangling/offline root — skip */ }
            catch (UnauthorizedAccessException) { /* inaccessible root — skip */ }
        }
        return found;
    }

    private static string NameOf(string path) =>
        System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                                 System.IO.Path.AltDirectorySeparatorChar));

    // Steam Workshop folders are named by numeric publishedid (e.g. 1559212036), so the folder name is
    // useless for display. Resolve the human name the way the official DayZ Launcher does: prefer the
    // Workshop name in meta.cpp (auto-written on publish — always present for subscribed mods), then the
    // author-written mod.cpp presentation name, then fall back to the folder name (local/dev mods).
    // Both files are plain `key = "value";` text in the mod root.
    public static string ResolveName(string path)
    {
        return NameFromCpp(System.IO.Path.Combine(path, "meta.cpp"))
            ?? NameFromCpp(System.IO.Path.Combine(path, "mod.cpp"))
            ?? NameOf(path);
    }

    private static readonly System.Text.RegularExpressions.Regex NameRx =
        new(@"(^|\s)name\s*=\s*""([^""]*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // First `name = "..."` assignment in a .cpp metadata file, or null (missing/unreadable/blank/no match).
    private static string? NameFromCpp(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var m = NameRx.Match(File.ReadAllText(file));
            if (!m.Success) return null;
            var name = m.Groups[2].Value.Trim();
            return name.Length > 0 ? name : null;
        }
        catch { return null; }
    }

    // Keep saved order/enabled/side, mark missing if gone, append newly-found as disabled.
    public static List<Mod> Merge(IReadOnlyList<ModEntry> saved, IReadOnlyList<string> discovered)
    {
        var present = new HashSet<string>(discovered, StringComparer.OrdinalIgnoreCase);
        var savedPaths = new HashSet<string>(saved.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
        var result = new List<Mod>();
        foreach (var s in saved)
            result.Add(new Mod(ResolveName(s.Path), s.Path, s.Enabled, s.Side, !present.Contains(s.Path)));
        foreach (var d in discovered)
            if (!savedPaths.Contains(d))
                result.Add(new Mod(ResolveName(d), d, false, "both", false));
        return result;
    }
}
