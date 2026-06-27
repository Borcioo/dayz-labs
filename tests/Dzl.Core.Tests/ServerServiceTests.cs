using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class ServerServiceTests
{
    [Fact]
    public void Creates_five_distinct_server_presets()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");

        Profiles.EnsureDefault(configPath);

        // point projects_root via base config; DayzPath=tmp means no real mission to copy — fine
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = root, DayzPath = tmp }, configPath);

        var svc = new ServerService(configPath);
        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            svc.Create(n, "chernarus").Ok.Should().BeTrue();

        var presets = Profiles.List(configPath);
        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            presets.Should().Contain(n);

        var ports = new[] { "alpha", "bravo", "charlie", "delta", "echo" }
            .Select(n => Profiles.Load(n, configPath).Port)
            .ToList();
        ports.Should().OnlyHaveUniqueItems();

        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            File.Exists(Path.Combine(root, "servers", n, "serverDZ.cfg")).Should().BeTrue();
    }

    [Fact]
    public void Create_points_serverDZcfg_template_at_the_instances_own_mission()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");
        var install = Path.Combine(tmp, "DayZ");
        // A real install mission to copy into the new instance.
        Directory.CreateDirectory(Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus"));

        Profiles.EnsureDefault(configPath);
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = root, DayzPath = install }, configPath);

        new ServerService(configPath).Create("alpha", "chernarus").Ok.Should().BeTrue();

        var instanceMission = Path.Combine(root, "servers", "alpha", "mpmissions", "dayzOffline.chernarusplus");
        File.ReadAllText(Path.Combine(root, "servers", "alpha", "serverDZ.cfg"))
            .Should().Contain($"template = \"{instanceMission}\"");
    }

    [Fact]
    public void Create_repoints_Mission_at_the_new_instance_not_the_active_presets()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");
        var install = Path.Combine(tmp, "DayZ");
        Directory.CreateDirectory(Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus"));

        Profiles.EnsureDefault(configPath);
        var (g, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(g with { ProjectsRoot = root, DayzPath = install }, configPath);

        // The active preset carries an ABSOLUTE Mission pointing at a DIFFERENT instance (as the editor's
        // "Browse" produces). A new instance must not inherit it.
        var (active, _, _) = Profiles.ResolveActive(configPath);
        var foreign = @"D:\DayzProjects\servers\test6\mpmissions\dayzOffline.chernarusplus";
        Profiles.Save(active with { Mission = foreign }, "default", configPath);

        new ServerService(configPath).Create("alpha", "chernarus").Ok.Should().BeTrue();

        var mission = Profiles.Load("alpha", configPath).Mission;
        mission.Should().NotContain("test6");
        mission.Should().Contain(Path.Combine("servers", "alpha", "mpmissions"));
    }
}
