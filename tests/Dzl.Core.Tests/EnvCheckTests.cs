using Dzl.Core.Config;
using Dzl.Core.Env;
using FluentAssertions;
using Xunit;

public class EnvCheckTests
{
    private static DzlConfig CfgIn(string root) => DzlConfig.Default() with {
        DayzPath = root,
        ProfilesPath = Path.Combine(root, "profiles"),
        ClientProfilesPath = Path.Combine(root, "profiles_client"),
        ConfigName = "serverDZ.cfg",
        Mission = "./mpmissions/dayzOffline.chernarusplus",
        ScanRoots = new() { Path.Combine(root, "mods") },
        DayzToolsPath = Path.Combine(root, "tools"),
    };

    [Fact]
    public void All_present_passes_core_checks()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(root, "DayZDiag_x64.exe"), "");
        File.WriteAllText(Path.Combine(root, "DayZ_x64.exe"), "");
        Directory.CreateDirectory(Path.Combine(root, "profiles"));
        Directory.CreateDirectory(Path.Combine(root, "profiles_client"));
        File.WriteAllText(Path.Combine(root, "serverDZ.cfg"), "");
        Directory.CreateDirectory(Path.Combine(root, "mpmissions", "dayzOffline.chernarusplus"));
        var items = EnvCheck.Run(CfgIn(root), () => true);
        items.Should().Contain(i => i.Key == "dayz_install" && i.Ok);
        items.Should().Contain(i => i.Key == "server_exe" && i.Ok);
        items.Should().Contain(i => i.Key == "profiles" && i.Ok);
        items.Should().Contain(i => i.Key == "server_cfg" && i.Ok);
        items.Should().Contain(i => i.Key == "mission" && i.Ok);
        items.Should().Contain(i => i.Key == "work_drive" && i.Ok);   // stubbed true
    }

    [Fact]
    public void Missing_install_flags_errors()
    {
        var items = EnvCheck.Run(CfgIn(@"X:\nope\dayz"), () => false);
        items.Should().Contain(i => i.Key == "dayz_install" && !i.Ok && i.Severity == CheckSeverity.Error);
        items.Should().Contain(i => i.Key == "server_exe" && !i.Ok);
        items.Should().Contain(i => i.Key == "work_drive" && !i.Ok);
    }

    [Fact]
    public void Reports_tools_registered_from_injected_func()
    {
        var cfg = CfgIn(Directory.CreateTempSubdirectory().FullName);
        EnvCheck.Run(cfg, () => true, () => false).Should().Contain(i => i.Key == "tools_registered" && !i.Ok);
        EnvCheck.Run(cfg, () => true, () => true ).Should().Contain(i => i.Key == "tools_registered" && i.Ok);
    }

    [Fact]
    public void Run_never_throws_on_bad_paths()
    {
        var act = () => EnvCheck.Run(DzlConfig.Default() with { DayzPath = "", ScanRoots = new() { "" } }, () => false);
        act.Should().NotThrow();
    }
}
