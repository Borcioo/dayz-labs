using System.IO;
using Dzl.Core.App;
using Dzl.Core.Config;

/// <summary>
/// Shared test scaffold: builds a throwaway dzl config that resolves to a temp DayZ mission, so the CE
/// services (and the VMs over them) read/write real files under a temp dir. Mirrors the Core
/// TypesServiceMultiFileTests scaffold.
/// </summary>
internal static class CeScaffold
{
    /// <summary>Create a temp mission and write each (relativePath, content) CE file into it (relative paths
    /// use '/'; nested dirs like "db/globals.xml" are created). Returns the config path.</summary>
    public static string Mission(params (string Relative, string Content)[] files)
    {
        var (configPath, missionDir) = Base();
        Directory.CreateDirectory(missionDir);
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(missionDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return configPath;
    }

    /// <summary>A valid config that resolves to NO active mission (no mpmissions dir). For VMs that build
    /// rows in memory (e.g. TypesEditorVm.AddType, whose SourceFile then resolves to "").</summary>
    public static string NoMission() => Base().configPath;

    private static (string configPath, string missionDir) Base()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(dir, "config.json");
        var instDir = Path.Combine(dir, "servers", "test");
        Directory.CreateDirectory(instDir);
        var cfgFile = Path.Combine(instDir, "serverDZ.cfg");
        File.WriteAllText(cfgFile, "");

        GlobalStore.Save(new GlobalConfig { ProjectsRoot = dir }, configPath);
        Profiles.Save(DzlConfig.Default() with { ConfigName = cfgFile }, "test", configPath);
        Profiles.SetActive("test", configPath);

        return (configPath, Path.Combine(instDir, "mpmissions", "dayzOffline.chernarusplus"));
    }
}
