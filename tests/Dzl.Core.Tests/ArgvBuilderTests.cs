using Dzl.Core.Config;
using Dzl.Core.Launch;
using FluentAssertions;

public class ArgvBuilderTests
{
    private static DzlConfig Sides() => DzlConfig.Default() with
    {
        DayzPath = @"E:\DayZ", ProfilesPath = @"E:\DayZ\profiles", ClientProfilesPath = @"E:\DayZ\profiles_client",
        Mods = new() {
            new() { Path = @"P:\@CF",    Enabled = true, Side = "both" },
            new() { Path = @"P:\@Admin", Enabled = true, Side = "server" },
            new() { Path = @"P:\@UI",    Enabled = true, Side = "client" },
        }
    };

    [Fact]
    public void Server_debug_splits_mod_and_servermod_and_has_server_flag()
    {
        var a = ArgvBuilder.Build("debug", "server", Sides());
        a.Should().Contain("-server");
        a.Should().Contain(@"-mod=P:\@CF");
        a.Should().Contain(@"-serverMod=P:\@Admin");
        string.Join(' ', a).Should().NotContain("@UI");
    }

    [Fact]
    public void Server_normal_has_no_server_flag()
        => ArgvBuilder.Build("normal", "server", Sides()).Should().NotContain("-server");

    [Fact]
    public void Client_has_both_and_client_mods_and_connect()
    {
        var a = ArgvBuilder.Build("debug", "client", Sides());
        var mod = a.Single(x => x.StartsWith("-mod="));
        mod.Should().Contain("@CF").And.Contain("@UI");
        mod.Should().NotContain("@Admin");
        a.Should().Contain("-connect=127.0.0.1").And.Contain("-name=DevMacie");
        a.Should().NotContain("-server");
    }

    [Fact]
    public void Profiles_relative_when_under_dayz_else_absolute()
    {
        ArgvBuilder.Build("debug", "server", Sides()).Should().Contain("-profiles=profiles");
        var outside = Sides() with { ProfilesPath = @"D:\custom\prof" };
        ArgvBuilder.Build("debug", "server", outside).Should().Contain(@"-profiles=D:\custom\prof");
    }

    [Fact]
    public void Per_mode_params_appended_last()
    {
        var cfg = Sides() with { ServerParamsDebug = new() { "-dbgFlag" }, ServerParamsNormal = new() { "-normFlag" } };
        ArgvBuilder.Build("debug", "server", cfg).Last().Should().Be("-dbgFlag");
        ArgvBuilder.Build("normal", "server", cfg).Last().Should().Be("-normFlag");
    }

    [Fact]
    public void Config_relative_value_kept_as_is()
        => ArgvBuilder.Build("debug", "server", Sides() with { ConfigName = "serverDZ.cfg" })
            .Should().Contain("-config=serverDZ.cfg");

    [Fact]
    public void Config_absolute_instance_path_kept_absolute()
        // DayZ 1.29 accepts an absolute -config (verified live), and the engine forces $currentdir to the
        // exe dir regardless of WorkingDir — so the only way the instance's own serverDZ.cfg is used is to
        // pass its absolute path.
        => ArgvBuilder.Build("debug", "server", Sides() with { ConfigName = @"D:\DayzProjects\servers\Test\serverDZ.cfg" })
            .Should().Contain(@"-config=D:\DayzProjects\servers\Test\serverDZ.cfg");

    [Fact]
    public void WorkingDir_is_install_for_relative_config_and_for_client()
    {
        var cfg = Sides();
        ArgvBuilder.WorkingDir(cfg, "server").Should().Be(cfg.DayzPath);
        ArgvBuilder.WorkingDir(cfg, "client").Should().Be(cfg.DayzPath);
    }

    [Fact]
    public void WorkingDir_is_instance_dir_for_rooted_existing_config()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "serverDZ.cfg"), "hostname=\"x\";");
        var cfg = Sides() with { ConfigName = Path.Combine(dir, "serverDZ.cfg") };
        ArgvBuilder.WorkingDir(cfg, "server").Should().Be(dir);
        ArgvBuilder.WorkingDir(cfg, "client").Should().Be(cfg.DayzPath);   // client always from install
    }
}
