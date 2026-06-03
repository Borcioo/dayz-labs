using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;
using Xunit;

public class LauncherServiceTests
{
    private static string TmpConfig() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");

    [Fact]
    public void Status_reports_active_profile_and_down_targets()
    {
        var svc = new LauncherService(TmpConfig());
        var s = svc.Status();
        s.ActivePreset.Should().Be("default");      // EnsureDefault seeded it
        s.Mode.Should().Be("debug");
        s.Port.Should().Be(2302);
        s.Server.State.Should().Be("down");
        s.Client.State.Should().Be("down");
    }

    [Fact]
    public void Mods_lists_enabled_with_side()
    {
        var path = TmpConfig();
        var cfg = ConfigStore.Load(path) with { Mods = new() {
            new() { Path = @"P:\@CF", Enabled = true, Side = "both" },
            new() { Path = @"P:\@Off", Enabled = false, Side = "both" },
        }};
        Profiles.EnsureDefault(path);
        ConfigStore.Save(cfg, Profiles.PresetFile("default", path)); // active profile is 'default'
        var svc = new LauncherService(path);
        var mods = svc.Mods();
        mods.Should().ContainSingle(m => m.Path == @"P:\@CF" && m.Side == "both");
    }

    [Fact]
    public void Presets_marks_active_and_set_preset_switches()
    {
        var path = TmpConfig();
        var svc = new LauncherService(path);
        svc.SaveActivePresetAs("alpha");
        svc.Presets().Should().Contain(p => p.Name == "alpha" && p.Active);
        svc.SetPreset("default");
        svc.Presets().Should().Contain(p => p.Name == "default" && p.Active);
        svc.SetPreset("ghost").Ok.Should().BeFalse();  // unknown preset
    }

    [Fact]
    public void Logs_returns_empty_when_no_file()
    {
        // Point profiles at empty temp dirs so resolution finds nothing,
        // independent of any real DayZ install on the test machine.
        var path = TmpConfig();
        var empty = Directory.CreateTempSubdirectory().FullName;
        var cfg = ConfigStore.Load(path) with { ProfilesPath = empty, ClientProfilesPath = empty };
        ConfigStore.Save(cfg, path);
        Profiles.EnsureDefault(path);
        ConfigStore.Save(cfg, Profiles.PresetFile("default", path)); // active profile is 'default'
        var svc = new LauncherService(path);
        svc.Logs("script", 10).Lines.Should().BeEmpty();
    }
}
