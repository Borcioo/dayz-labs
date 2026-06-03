using Dzl.Core.Config;
using Dzl.Core.Launch;
using FluentAssertions;
using Xunit;

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
}
