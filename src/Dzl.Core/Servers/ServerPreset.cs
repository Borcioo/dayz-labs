using Dzl.Core.Config;

namespace Dzl.Core.Servers;

/// <summary>Pure: a copy of <paramref name="baseCfg"/> with the server-instance-specific fields
/// (profiles, client profiles, serverDZ.cfg path, port) repointed at <paramref name="instanceDir"/>.
/// Everything else (DayZ paths, exes, params, mods) is inherited from baseCfg.</summary>
public static class ServerPreset
{
    public static DzlConfig Build(DzlConfig baseCfg, string instanceDir, int port) =>
        baseCfg with
        {
            ProfilesPath       = Path.Combine(instanceDir, "profiles"),
            ClientProfilesPath = Path.Combine(instanceDir, "profiles_client"),
            ConfigName         = Path.Combine(instanceDir, "serverDZ.cfg"),
            Port               = port,
        };
}
