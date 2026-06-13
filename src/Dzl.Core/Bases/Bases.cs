using System.Text.Json;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;

namespace Dzl.Core.Bases;

/// <summary>Metadata for a server <b>base</b> (template): a reusable set of mission + serverDZ.cfg under
/// <c>&lt;ProjectsRoot&gt;\bases\&lt;name&gt;\</c> that new instances can be created from. Stored at
/// <c>&lt;base&gt;\.dzl\base.json</c>.</summary>
public sealed record BaseInfo
{
    public string Name { get; init; } = "";
    public string Source { get; init; } = "custom";   // "dayz-install" | "custom"
    public string DayzVersion { get; init; } = "";     // captured for dayz-install bases
    public string Mission { get; init; } = "";
}

/// <summary>Manages server bases (templates). New server instances can copy from a base instead of the
/// raw DayZ install — so users build/version their own templates.</summary>
/// <remarks>A base lives at <c>&lt;ProjectsRoot&gt;\bases\&lt;name&gt;\</c> with its <c>serverDZ.cfg</c> +
/// <c>mpmissions\</c> and a <c>.dzl\base.json</c> descriptor.</remarks>
public static class ServerBases
{
    private const string DzlDir = ".dzl";
    private const string BaseFileName = "base.json";

    public static string BasesDir(string root) => Path.Combine(root, "bases");
    public static string BaseDir(string root, string name) => Path.Combine(BasesDir(root), name);
    public static string BaseFile(string root, string name) => Path.Combine(BaseDir(root, name), DzlDir, BaseFileName);

    public static bool Exists(string root, string name) => File.Exists(BaseFile(root, name));

    public static List<BaseInfo> List(string root)
    {
        var dir = BasesDir(root);
        var list = new List<BaseInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var d in Directory.GetDirectories(dir))
        {
            var f = Path.Combine(d, DzlDir, BaseFileName);
            if (!File.Exists(f)) continue;
            try
            {
                var info = JsonSerializer.Deserialize<BaseInfo>(File.ReadAllText(f), ConfigStore.Json) ?? new BaseInfo();
                list.Add(info with { Name = Path.GetFileName(d) });
            }
            catch { /* skip unreadable */ }
        }
        return list.OrderBy(b => b.Name).ToList();
    }

    private static void WriteInfo(string root, string name, BaseInfo info)
    {
        var f = BaseFile(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, JsonSerializer.Serialize(info with { Name = name }, ConfigStore.Json));
    }

    /// <summary>Create an empty/custom base — a skeleton the user fills with their own mpmissions + serverDZ.cfg.</summary>
    public static (bool ok, string message) CreateEmpty(string root, string name)
    {
        if (!ProjectPaths.IsValidName(name)) return (false, $"invalid base name: {name}");
        if (Exists(root, name)) return (false, $"base '{name}' already exists");
        var dir = BaseDir(root, name);
        Directory.CreateDirectory(Path.Combine(dir, "mpmissions"));
        WriteInfo(root, name, new BaseInfo { Source = "custom" });
        return (true, $"created empty base '{name}' — add your mpmissions + serverDZ.cfg under {dir}");
    }

    /// <summary>Create a base by snapshotting the DayZ install's mission (minus live storage) + a default
    /// serverDZ.cfg, tagged with the install's DayZ version.</summary>
    public static (bool ok, string message) CreateFromInstall(string root, string name, string dayzPath, string missionName)
    {
        if (!ProjectPaths.IsValidName(name)) return (false, $"invalid base name: {name}");
        if (Exists(root, name)) return (false, $"base '{name}' already exists");
        var dir = BaseDir(root, name);
        try
        {
            Directory.CreateDirectory(dir);
            var cfg = Path.Combine(dir, "serverDZ.cfg");
            if (!File.Exists(cfg)) File.WriteAllText(cfg, ServerScaffold.DefaultServerCfg(missionName));

            var src = Path.Combine(dayzPath, "mpmissions", missionName);
            var dst = Path.Combine(dir, "mpmissions", missionName);
            if (Directory.Exists(src) && !Directory.Exists(dst)) ServerScaffold.CopyMission(src, dst);

            var ver = EnvDetect.DayzVersion(dayzPath);
            WriteInfo(root, name, new BaseInfo { Source = "dayz-install", DayzVersion = ver, Mission = missionName });
            return (true, $"created base '{name}' from DayZ install (mission {missionName}, DayZ {ver})"
                          + (Directory.Exists(src) ? "" : "  [warning: mission not found in install]"));
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static bool Delete(string root, string name)
    {
        var dir = BaseDir(root, name);
        if (!Directory.Exists(dir)) return false;
        try { Directory.Delete(dir, recursive: true); return true; } catch { return false; }
    }

    /// <summary>Copy a base's serverDZ.cfg + mpmissions into a new instance dir (skips storage, won't clobber).</summary>
    public static void CopyInto(string root, string name, string instanceDir)
    {
        var bdir = BaseDir(root, name);
        Directory.CreateDirectory(instanceDir);

        var cfg = Path.Combine(bdir, "serverDZ.cfg");
        var cfgDst = Path.Combine(instanceDir, "serverDZ.cfg");
        if (File.Exists(cfg) && !File.Exists(cfgDst)) File.Copy(cfg, cfgDst);

        var mpm = Path.Combine(bdir, "mpmissions");
        if (Directory.Exists(mpm))
            foreach (var mission in Directory.GetDirectories(mpm))
            {
                var d = Path.Combine(instanceDir, "mpmissions", Path.GetFileName(mission));
                if (!Directory.Exists(d)) ServerScaffold.CopyMission(mission, d);
            }
    }
}
