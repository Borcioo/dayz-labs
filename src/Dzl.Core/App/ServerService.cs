using Dzl.Core.Bases;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Servers;

namespace Dzl.Core.App;

public sealed record CreateServerResult(bool Ok, string Name, string Dir, int Port, string Message);

public sealed class ServerService
{
    private readonly string _configPath;
    public ServerService(string configPath) { _configPath = configPath; }

    /// <summary>All server instances = the per-server configs under instances/ (after SP8, an
    /// instance IS the profile). Each entry's Dir/CfgPath come from its serverDZ.cfg path.</summary>
    public IReadOnlyList<ServerInstance> List()
    {
        var list = new List<ServerInstance>();
        foreach (var name in Profiles.List(_configPath))
        {
            try
            {
                var cfg = Profiles.Load(name, _configPath);
                var cfgPath = cfg.ConfigName;
                var dir = Path.GetDirectoryName(cfgPath) ?? "";
                list.Add(new ServerInstance(name, dir, cfgPath));
            }
            catch { /* skip unreadable instance */ }
        }
        return list;
    }

    /// <summary>Scaffold a new server instance (from a base/template if given, else from the DayZ install)
    /// and save it as a preset (atomically), optionally activating it.</summary>
    public CreateServerResult Create(string name, string map, int? port = null, bool activate = true, string? baseName = null)
    {
        Profiles.EnsureDefault(_configPath);
        if (!ProjectPaths.IsValidName(name))
            return new CreateServerResult(false, name, "", 0, $"invalid instance name: {name}");

        var (baseCfg, _, _) = Profiles.ResolveActive(_configPath);
        var root = ProjectPaths.Root(baseCfg);
        var instanceDir = ProjectPaths.ServerDir(root, name);
        var template = MapAliases.MissionTemplate(map);

        var usedPorts = Profiles.List(_configPath)
            .Select(n => { try { return Profiles.Load(n, _configPath).Port; } catch { return 0; } })
            .Where(p => p > 0);
        var chosenPort = port ?? ServerInstances.NextPort(usedPorts);

        string sourceNote;
        if (!string.IsNullOrWhiteSpace(baseName) && ServerBases.Exists(root, baseName!))
        {
            // From a base/template: copy its serverDZ.cfg + mpmissions, then add the runtime bits.
            ServerBases.CopyInto(root, baseName!, instanceDir);
            Directory.CreateDirectory(Path.Combine(instanceDir, "profiles"));
            Directory.CreateDirectory(Path.Combine(instanceDir, "profiles_client"));
            ServerScaffold.EnsureFilePatching(Path.Combine(instanceDir, "serverDZ.cfg"));
            sourceNote = $"from base '{baseName}'";
        }
        else
        {
            var report = ServerScaffold.Scaffold(baseCfg.DayzPath, instanceDir, template);
            sourceNote = $"from DayZ install; {report.Notes}".TrimEnd(';', ' ');
        }

        var cfg = ServerPreset.Build(baseCfg, instanceDir, chosenPort);
        Profiles.Save(cfg, name, _configPath);
        if (activate) Profiles.SetActive(name, _configPath);

        return new CreateServerResult(true, name, instanceDir, chosenPort, $"instance created {sourceNote}");
    }

    /// <summary>List the available bases (templates) under the active ProjectsRoot.</summary>
    public IReadOnlyList<BaseInfo> ListBases()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return ServerBases.List(ProjectPaths.Root(cfg));
    }
}
