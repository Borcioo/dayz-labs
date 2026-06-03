using Dzl.Core.Config;

namespace Dzl.Core.Mods;

public sealed record Mod(string Name, string Path, bool Enabled, string Side, bool Missing);

public static class ModDiscovery
{
    public static bool IsMod(string dir) =>
        Directory.Exists(System.IO.Path.Combine(dir, "addons"))
        || (File.Exists(System.IO.Path.Combine(dir, "config.cpp"))
            && Directory.Exists(System.IO.Path.Combine(dir, "scripts")));

    // All immediate subdirectories of each root that look like a mod.
    public static List<string> Discover(IEnumerable<string> roots)
    {
        var found = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
                if (IsMod(dir)) found.Add(dir);
        }
        return found;
    }

    private static string NameOf(string path) =>
        System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                                 System.IO.Path.AltDirectorySeparatorChar));

    // Keep saved order/enabled/side, mark missing if gone, append newly-found as disabled.
    public static List<Mod> Merge(IReadOnlyList<ModEntry> saved, IReadOnlyList<string> discovered)
    {
        var present = new HashSet<string>(discovered, StringComparer.OrdinalIgnoreCase);
        var savedPaths = new HashSet<string>(saved.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
        var result = new List<Mod>();
        foreach (var s in saved)
            result.Add(new Mod(NameOf(s.Path), s.Path, s.Enabled, s.Side, !present.Contains(s.Path)));
        foreach (var d in discovered)
            if (!savedPaths.Contains(d))
                result.Add(new Mod(NameOf(d), d, false, "both", false));
        return result;
    }
}
