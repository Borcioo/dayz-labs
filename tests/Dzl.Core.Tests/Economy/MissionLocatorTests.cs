using Dzl.Core.Config;
using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

public class MissionLocatorTests
{
    private static (DzlConfig cfg, string missionDir) Scaffold(string missionRel)
    {
        var root = Path.Combine(Path.GetTempPath(), "dzl-mission-" + Guid.NewGuid().ToString("N"));
        var instanceDir = Path.Combine(root, "servers", "Test");
        var missionDir = Path.Combine(instanceDir, "mpmissions", "dayzOffline.chernarusplus");
        Directory.CreateDirectory(Path.Combine(missionDir, "db"));
        var cfg = new DzlConfig { ConfigName = Path.Combine(instanceDir, "serverDZ.cfg"), Mission = missionRel };
        return (cfg, missionDir);
    }

    [Fact]
    public void Resolve_uses_cfg_Mission_relative_to_the_instance_dir()
    {
        var (cfg, missionDir) = Scaffold("./mpmissions/dayzOffline.chernarusplus");
        var paths = MissionLocator.Resolve(cfg);
        paths.Should().NotBeNull();
        paths!.MissionDir.Should().Be(missionDir);
        paths.Db.Should().Be(Path.Combine(missionDir, "db"));
        paths.EconomyCore.Should().Be(Path.Combine(missionDir, "cfgeconomycore.xml"));
    }

    [Fact]
    public void Resolve_returns_null_when_ConfigName_is_not_rooted()
    {
        var cfg = new DzlConfig { ConfigName = "serverDZ.cfg", Mission = "./mpmissions/x" };
        MissionLocator.Resolve(cfg).Should().BeNull();
    }

    [Fact]
    public void Resolve_falls_back_to_first_mpmissions_child_when_Mission_missing()
    {
        var (cfg, missionDir) = Scaffold("");
        MissionLocator.Resolve(cfg)!.MissionDir.Should().Be(missionDir);
    }
}
