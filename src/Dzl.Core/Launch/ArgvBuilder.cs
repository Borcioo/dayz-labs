using Dzl.Core.Config;

namespace Dzl.Core.Launch;

public static class ArgvBuilder
{
    public static List<string> ModsForTarget(DzlConfig c, string target) =>
        c.Mods.Where(m => m.Enabled).Where(m =>
            (target == "server" && m.Side == "both") ||
            (target == "client" && (m.Side is "both" or "client")))
         .Select(m => m.Path).ToList();

    public static List<string> ServerOnlyMods(DzlConfig c) =>
        c.Mods.Where(m => m.Enabled && m.Side == "server").Select(m => m.Path).ToList();

    public static List<string> ParamsFor(DzlConfig c, string target, string mode) => (target, mode) switch
    {
        ("server", "debug")  => c.ServerParamsDebug,
        ("server", "normal") => c.ServerParamsNormal,
        ("client", "debug")  => c.ClientParamsDebug,
        ("client", "normal") => c.ClientParamsNormal,
        _ => new()
    };

    private static string ProfilesArg(string path, string dayz)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(dayz);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    public static List<string> Build(string mode, string target, DzlConfig c)
    {
        if (target == "server")
        {
            var a = new List<string>();
            if (mode == "debug") a.Add("-server");
            a.Add($"-profiles={ProfilesArg(c.ProfilesPath, c.DayzPath)}");
            a.Add($"-mod={string.Join(';', ModsForTarget(c, "server"))}");
            a.Add($"-config={c.ConfigName}");
            a.Add($"-port={c.Port}");
            var so = ServerOnlyMods(c);
            if (so.Count > 0) a.Add($"-serverMod={string.Join(';', so)}");
            a.AddRange(ParamsFor(c, "server", mode));
            return a;
        }
        if (target == "client")
        {
            var a = new List<string>
            {
                $"-profiles={ProfilesArg(c.ClientProfilesPath, c.DayzPath)}",
                $"-mod={string.Join(';', ModsForTarget(c, "client"))}",
                $"-mission={c.Mission}",
                $"-connect={c.ConnectIp}",
                $"-port={c.Port}",
                $"-name={c.PlayerName}",
            };
            a.AddRange(ParamsFor(c, "client", mode));
            return a;
        }
        throw new ArgumentException($"unknown target: {target}");
    }
}
