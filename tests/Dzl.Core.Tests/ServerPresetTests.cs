using Dzl.Core.Config;
using Dzl.Core.Servers;
using FluentAssertions;
public class ServerPresetTests
{
    [Fact]
    public void Build_points_config_at_the_instance()
    {
        var baseCfg = DzlConfig.Default() with { Port = 9999 };
        var cfg = ServerPreset.Build(baseCfg, instanceDir: @"D:\P\servers\alpha", port: 2304);

        cfg.ProfilesPath.Should().Be(@"D:\P\servers\alpha\profiles");
        cfg.ClientProfilesPath.Should().Be(@"D:\P\servers\alpha\profiles_client");
        cfg.ConfigName.Should().Be(@"D:\P\servers\alpha\serverDZ.cfg");
        cfg.Port.Should().Be(2304);
        cfg.DayzPath.Should().Be(baseCfg.DayzPath);
    }
}
