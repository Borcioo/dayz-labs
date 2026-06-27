using Dzl.Core.Config;
using Dzl.Core.Economy;
using FluentAssertions;

public class MissionCheckTests
{
    // Build an instance dir (with serverDZ.cfg + its own mpmissions) and a separate DayZ install
    // dir (with its own mpmissions), so the two mission locations are distinguishable.
    private static (DzlConfig cfg, string instanceMission, string installMission) Setup(string template)
    {
        var root = Path.Combine(Path.GetTempPath(), "dzl-mc-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "DayZ");
        var instanceDir = Path.Combine(root, "servers", "test");
        var installMission = Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus");
        var instanceMission = Path.Combine(instanceDir, "mpmissions", "dayzOffline.chernarusplus");
        Directory.CreateDirectory(installMission);
        Directory.CreateDirectory(instanceMission);
        var cfgPath = Path.Combine(instanceDir, "serverDZ.cfg");
        File.WriteAllText(cfgPath, $"class Missions{{class DayZ{{template = \"{template}\";}};}};");
        var cfg = new DzlConfig
        {
            DayzPath = install,
            ConfigName = cfgPath,
            Mission = Path.Combine(instanceDir, "mpmissions", "dayzOffline.chernarusplus"),
        };
        return (cfg, instanceMission, installMission);
    }

    [Fact]
    public void Absolute_template_pointing_at_instance_mission_is_Instance()
    {
        var (cfg, instanceMission, _) = Setup("PLACEHOLDER");
        File.WriteAllText(cfg.ConfigName, $"template = \"{instanceMission}\";");

        var r = MissionCheck.Evaluate(cfg);

        r.Status.Should().Be(MissionSourceStatus.Instance);
        r.EffectivePath.Should().Be(instanceMission);
        r.Fixable.Should().BeFalse("already points at the instance — nothing to fix");
    }

    [Fact]
    public void Bare_name_template_loads_install_mpmissions_so_status_is_Install()
    {
        var (cfg, _, installMission) = Setup("dayzOffline.chernarusplus");

        var r = MissionCheck.Evaluate(cfg);

        r.Status.Should().Be(MissionSourceStatus.Install);
        r.EffectivePath.Should().Be(installMission);
        r.Fixable.Should().BeTrue("the instance has its own mission to point the template at");
    }

    [Fact]
    public void Bare_name_with_no_such_folder_is_Missing()
    {
        var (cfg, _, installMission) = Setup("dayzOffline.sakhal");   // install only has chernarusplus
        Directory.Exists(Path.Combine(Path.GetDirectoryName(installMission)!, "dayzOffline.sakhal"))
            .Should().BeFalse();

        MissionCheck.Evaluate(cfg).Status.Should().Be(MissionSourceStatus.Missing);
    }

    [Fact]
    public void Relative_ConfigName_cannot_be_resolved_so_status_is_Unknown()
    {
        var cfg = new DzlConfig { ConfigName = "serverDZ.cfg" };
        MissionCheck.Evaluate(cfg).Status.Should().Be(MissionSourceStatus.Unknown);
    }
}
