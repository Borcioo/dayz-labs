using Dzl.Core.App;
using Dzl.Core.Build;
using Dzl.Core.Config;
using Dzl.Core.Projects;
using FluentAssertions;
using Xunit;

public class ModBuildTests
{
    // config.json with a temp ProjectsRoot + a temp (empty) DayZ Tools path so AddonBuilder is absent.
    private static (string configPath, string root) TmpConfig()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var root = Path.Combine(dir, "projects");
        var configPath = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = root, DayzToolsPath = Path.Combine(dir, "tools") }, configPath);
        return (configPath, root);
    }

    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    // --- fresh-pbo verification ---

    [Fact]
    public void HasFreshPbo_true_only_for_a_pbo_written_after_the_start()
    {
        var addons = Tmp();
        var pbo = Path.Combine(addons, "Foo.pbo");
        File.WriteAllText(pbo, "x");
        var written = File.GetLastWriteTimeUtc(pbo);

        ModBuild.HasFreshPbo(addons, written.AddSeconds(-5)).Should().BeTrue();   // started before the pbo
        ModBuild.HasFreshPbo(addons, written.AddSeconds(5)).Should().BeFalse();   // started after = stale
    }

    [Fact]
    public void HasFreshPbo_false_when_no_pbo_present()
    {
        ModBuild.HasFreshPbo(Tmp(), DateTime.UtcNow.AddMinutes(-1)).Should().BeFalse();
        ModBuild.NewestPbo(Path.Combine(Tmp(), "missing")).Should().BeNull();
    }

    // --- run-list registration (pure) ---

    [Fact]
    public void Register_appends_enabled_mod_once_and_dedupes_case_insensitively()
    {
        var cfg = DzlConfig.Default();
        var a = ModBuild.Register(cfg, @"P:\Mods\@Foo");
        a.Mods.Should().ContainSingle(m => m.Path == @"P:\Mods\@Foo" && m.Enabled && m.Side == "both");

        var b = ModBuild.Register(a, @"p:\mods\@foo");   // same path, different case
        b.Should().BeSameAs(a);                          // no duplicate, original returned
        b.Mods.Should().HaveCount(1);
    }

    [Fact]
    public void Register_preserves_existing_mods_and_order()
    {
        var cfg = DzlConfig.Default() with
        {
            Mods = new List<ModEntry> { new() { Path = @"P:\Mods\@Other", Enabled = true } }
        };
        var r = ModBuild.Register(cfg, @"P:\Mods\@Foo");
        r.Mods.Select(m => m.Path).Should().ContainInOrder(@"P:\Mods\@Other", @"P:\Mods\@Foo");
    }

    // --- BuildService pre-flight failures (no AddonBuilder run, no P: touched) ---

    [Fact]
    public void Build_rejects_invalid_name()
    {
        var (configPath, _) = TmpConfig();
        var r = new BuildService(configPath).Build("1bad");
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("invalid mod name");
    }

    [Fact]
    public void Build_fails_when_not_a_mod_project()
    {
        var (configPath, _) = TmpConfig();
        var r = new BuildService(configPath).Build("Ghost");
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("not a mod project");
    }

    [Fact]
    public void Build_fails_when_addonbuilder_missing_even_for_a_valid_project()
    {
        var (configPath, root) = TmpConfig();
        var proj = ProjectPaths.ModDir(root, "Foo");   // <root>\mods\Foo
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, "config.cpp"), "class CfgPatches {};");

        var r = new BuildService(configPath).Build("Foo");
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("AddonBuilder not found");
        r.Registered.Should().BeFalse();
    }
}
