using Dzl.Core.Config;

namespace Dzl.Core.Economy;

public sealed record MissionPaths(string MissionDir, string EconomyCore, string Db, string? Vanilla);

/// <summary>Resolves the active server instance's mission folder + well-known CE paths. Pure given the config.</summary>
public static class MissionLocator
{
    public static MissionPaths? Resolve(DzlConfig cfg)
    {
        var cfgPath = cfg.ConfigName;
        if (string.IsNullOrWhiteSpace(cfgPath) || !Path.IsPathRooted(cfgPath)) return null;
        var instanceDir = Path.GetDirectoryName(cfgPath);
        if (instanceDir is null) return null;

        string? missionDir = null;
        if (!string.IsNullOrWhiteSpace(cfg.Mission))
        {
            var rel = cfg.Mission.Replace('/', Path.DirectorySeparatorChar).TrimStart('.', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(instanceDir, rel));
            if (Directory.Exists(candidate)) missionDir = candidate;
        }
        if (missionDir is null)
        {
            var mp = Path.Combine(instanceDir, "mpmissions");
            if (Directory.Exists(mp))
                missionDir = Directory.GetDirectories(mp).FirstOrDefault();
        }
        if (missionDir is null) return null;

        var db = Path.Combine(missionDir, "db");
        var vanilla = Path.Combine(db, "types.xml");
        return new MissionPaths(
            missionDir,
            Path.Combine(missionDir, "cfgeconomycore.xml"),
            db,
            File.Exists(vanilla) ? vanilla : null);
    }
}
