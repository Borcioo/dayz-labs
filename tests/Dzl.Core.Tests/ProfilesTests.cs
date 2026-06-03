using Dzl.Core.Config;
using FluentAssertions;
using Xunit;

public class ProfilesTests
{
    private static string TmpConfig() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");

    [Fact]
    public void Save_then_resolve_active_points_at_preset()
    {
        var path = TmpConfig();
        var c = DzlConfig.Default() with { Port = 2700 };
        Profiles.Save(c, "proj", path);
        Profiles.SetActive("proj", path);
        var (cfg, savePath, name) = Profiles.ResolveActive(path);
        name.Should().Be("proj");
        cfg.Port.Should().Be(2700);
        savePath.Should().Be(Profiles.PresetFile("proj", path));
    }

    [Fact]
    public void EnsureDefault_seeds_and_activates_on_first_run()
    {
        var path = TmpConfig();
        ConfigStore.Save(DzlConfig.Default() with { Port = 2345 }, path);
        Profiles.EnsureDefault(path).Should().Be("default");
        Profiles.List(path).Should().Contain("default");
        var (cfg, _, active) = Profiles.ResolveActive(path);
        active.Should().Be("default");
        cfg.Port.Should().Be(2345);
    }

    [Fact]
    public void EnsureDefault_noop_when_preset_already_active()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "proj", path);
        Profiles.SetActive("proj", path);
        Profiles.EnsureDefault(path).Should().Be("proj");
        Profiles.List(path).Should().NotContain("default");
    }

    [Fact]
    public void EnsureDefault_noop_when_presets_exist_but_none_active()
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
        savePath.Should().Be(path);
    }

    [Fact]
    public void Delete_removes_preset()
    {
        var path = TmpConfig();
        Profiles.Save(DzlConfig.Default(), "tmp", path);
        Profiles.Delete("tmp", path).Should().BeTrue();
        Profiles.List(path).Should().NotContain("tmp");
        Profiles.Delete("tmp", path).Should().BeFalse();
    }
}
