using System.IO;

namespace Dzl.Tray;

/// <summary>Where a "browse" dialog (folder/file picker) should open: the directory the field already points
/// at, so the picker lands next to the current value instead of always jumping to the DayZ install. Pure;
/// the filesystem is injected via <paramref name="exists"/> so it's unit-testable.</summary>
public static class BrowseStartDir
{
    public static string Resolve(string? current, bool isFile, IEnumerable<string?> fallbacks, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            string? full = null;
            try { if (Path.IsPathRooted(current)) full = Path.GetFullPath(current); } catch { /* bad path */ }
            if (full is not null)
            {
                if (!isFile && exists(full)) return full;            // an existing folder field → open in it
                var parent = SafeParent(full);                       // a file (or missing folder) → its parent
                if (parent is not null && exists(parent)) return parent;
            }
        }
        foreach (var f in fallbacks)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            try { var full = Path.GetFullPath(f); if (exists(full)) return full; } catch { /* skip */ }
        }
        return "";
    }

    private static string? SafeParent(string p) { try { return Path.GetDirectoryName(p); } catch { return null; } }
}
