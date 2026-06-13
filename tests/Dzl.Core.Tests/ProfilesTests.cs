using Dzl.Core.Config;
using FluentAssertions;

public class ProfilesTests
{
    // A config.json with a temp ProjectsRoot, so instances live under <temp>\projects\servers\<name>\.dzl\.
    private static string TmpConfig()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(dir, "projects") }, configPath);
        return configPath;
    }

    [Fact]
    public void Save_then_resolve_active_points_at_instance()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default() with { Port = 2700 }, "proj", path);
        Profiles.SetActive("proj", path);
        var (cfg, savePath, name) = Profiles.ResolveActive(path);
        name.Should().Be("proj");
        cfg.Port.Should().Be(2700);
        savePath.Should().Be(Profiles.PresetFile("proj", path));
    }

    [Fact]
    public void Instance_config_lives_in_dot_dzl_inside_its_folder()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "srv", path);
        var f = Profiles.PresetFile("srv", path);
        f.Should().EndWith(Path.Combine("servers", "srv", ".dzl", "instance.json"));
        File.Exists(f).Should().BeTrue();
    }

    [Fact]
    public void EnsureDefault_seeds_and_activates_on_first_run()
    {
        var path = TmpConfig();
        Profiles.EnsureDefault(path).Should().Be("default");
        Profiles.List(path).Should().Contain("default");
        var (_, _, active) = Profiles.ResolveActive(path);
        active.Should().Be("default");
    }

    [Fact]
    public void EnsureDefault_noop_when_instance_already_active()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "proj", path);
        Profiles.SetActive("proj", path);
        Profiles.EnsureDefault(path).Should().Be("proj");
        Profiles.List(path).Should().NotContain("default");
    }

    [Fact]
    public void EnsureDefault_noop_when_instances_exist_but_none_active()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "proj", path);
        Profiles.EnsureDefault(path).Should().Be("");
        Profiles.List(path).Should().NotContain("default");
    }

    [Fact]
    public void EnsureDefault_is_idempotent()
    {
        var path = TmpConfig();
        Profiles.EnsureDefault(path);
        Profiles.EnsureDefault(path);
        Profiles.List(path).Should().BeEquivalentTo(new[] { "default" });
    }

    [Fact]
    public void ResolveActive_ignores_dangling_pointer()
    {
        var path = TmpConfig();
        Profiles.SetActive("ghost", path);
        var (_, savePath, name) = Profiles.ResolveActive(path);
        name.Should().Be("");
        savePath.Should().Be(Profiles.PresetFile("default", path));
    }

    [Fact]
    public void Switching_active_instance_moves_the_save_target()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default() with { Port = 1 }, "alpha", path);
        Profiles.Save(DzlConfig.Default() with { Port = 2 }, "beta", path);

        Profiles.SetActive("alpha", path);
        var a = Profiles.ResolveActive(path);
        a.savePath.Should().Be(Profiles.PresetFile("alpha", path));

        Profiles.SetActive("beta", path);
        var b = Profiles.ResolveActive(path);
        b.savePath.Should().Be(Profiles.PresetFile("beta", path));
        b.savePath.Should().NotBe(a.savePath);
        b.cfg.Port.Should().Be(2);
    }

    [Fact]
    public void Delete_removes_instance()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "tmp", path);
        Profiles.Delete("tmp", path).Should().BeTrue();
        Profiles.List(path).Should().NotContain("tmp");
        Profiles.Delete("tmp", path).Should().BeFalse();
    }

    [Fact]
    public void Delete_keeps_files_by_default_but_purges_with_removeFolder()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "srv", path);
        var dir = Profiles.InstanceDir("srv", path);
        File.WriteAllText(Path.Combine(dir, "serverDZ.cfg"), "x");   // a server file alongside .dzl

        // default: instance gone from the list, but the folder + files remain
        Profiles.Delete("srv", path).Should().BeTrue();
        Profiles.List(path).Should().NotContain("srv");
        Directory.Exists(dir).Should().BeTrue();
        File.Exists(Path.Combine(dir, "serverDZ.cfg")).Should().BeTrue();

        // removeFolder: the whole instance folder is deleted
        Profiles.Save(DzlConfig.Default(), "srv2", path);
        var dir2 = Profiles.InstanceDir("srv2", path);
        File.WriteAllText(Path.Combine(dir2, "serverDZ.cfg"), "x");
        Profiles.Delete("srv2", path, removeFolder: true).Should().BeTrue();
        Directory.Exists(dir2).Should().BeFalse();
    }

    [Fact]
    public void Delete_does_not_resurrect_from_a_flat_backup()
    {
        var path = TmpConfig();
        // a leftover flat backup (from migration) + the per-folder instance, as on a migrated machine
        var flatDir = Path.Combine(Path.GetDirectoryName(path)!, "instances");
        Directory.CreateDirectory(flatDir);
        File.WriteAllText(Path.Combine(flatDir, "srv.json"), "{ \"port\": 2400 }");
        Profiles.Save(DzlConfig.Default() with { Port = 2400 }, "srv", path);
        Profiles.List(path).Should().Contain("srv");

        Profiles.Delete("srv", path).Should().BeTrue();
        Profiles.ResolveActive(path);                       // runs migration — must NOT recreate it
        Profiles.List(path).Should().NotContain("srv");
    }
}
