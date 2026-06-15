using Dzl.Core.Config;
using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>One auto-generated map-data file in a mission (building/cluster spawn positions). These are
/// exported in-game (GetCEApi ExportProxyData / ExportClusterData), not hand-edited — the tray surfaces them
/// as open-externally shortcuts, so this carries just enough to display + locate the file.</summary>
public sealed record MapFileInfo(string Name, string Path, long Bytes, string Description);

/// <summary>Locate the auto-generated map-data files (<c>map*.xml</c>) of the active mission. Pure listing
/// (<see cref="ListIn"/>) is unit-tested; <see cref="Files"/> resolves the mission first.</summary>
public sealed class MapFilesService
{
    private readonly string _configPath;
    public MapFilesService(string configPath) => _configPath = configPath;

    /// <summary>The active mission directory, or null when none is resolvable.</summary>
    public string? MissionDir()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return MissionLocator.Resolve(cfg)?.MissionDir;
    }

    /// <summary>The mission's map-data files (empty when no mission / none present).</summary>
    public IReadOnlyList<MapFileInfo> Files() => MapFiles.ListIn(MissionDir());
}

/// <summary>Pure helpers over a mission directory's map-data files.</summary>
public static class MapFiles
{
    /// <summary>All <c>map*.xml</c> files in <paramref name="dir"/> (mapgroup*/mapcluster*), sorted by name,
    /// each with a friendly description. Empty when the directory is null/missing.</summary>
    public static IReadOnlyList<MapFileInfo> ListIn(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return Array.Empty<MapFileInfo>();
        return Directory.EnumerateFiles(dir, "map*.xml", SearchOption.TopDirectoryOnly)
            .Select(p =>
            {
                var name = Path.GetFileName(p);
                long bytes; try { bytes = new FileInfo(p).Length; } catch { bytes = 0; }
                return new MapFileInfo(name, p, bytes, Describe(name));
            })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Friendly one-line description for a known map-data filename (prefix match, case-insensitive).</summary>
    public static string Describe(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.StartsWith("mapgrouppos")) return "Building instance positions on the terrain (exported).";
        if (n.StartsWith("mapgroupproto")) return "Loot-point prototype definitions per building type.";
        if (n.StartsWith("mapgroupcluster")) return "Cluster spawns — fruit trees, sticks, stones (exported).";
        if (n.StartsWith("mapclusterproto")) return "Cluster prototype definitions.";
        if (n.StartsWith("mapgroupdirt")) return "Dirt/ground cluster spawn data (exported).";
        return "Auto-generated map-data file.";
    }
}
