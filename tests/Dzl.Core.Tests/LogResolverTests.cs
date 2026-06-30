using Dzl.Core.Logs;
using FluentAssertions;

public class LogResolverTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Resolves_newest_of_each_server_log_type_plus_console()
    {
        var server = Tmp();
        var client = Tmp();
        File.WriteAllText(Path.Combine(server, "script_2026-06-30_10-00-00.log"), "");
        File.WriteAllText(Path.Combine(server, "DayZDiag_x64_2026-06-30_10-00-00.RPT"), "");
        File.WriteAllText(Path.Combine(server, "DayZDiag_x64_2026-06-30_10-00-00.ADM"), "");
        File.WriteAllText(Path.Combine(server, "server_console.log"), "");
        File.WriteAllText(Path.Combine(client, "script_2026-06-30_12-00-00.log"), "");

        var r = LogResolver.Resolve(server, client);

        r["script"].Should().EndWith("script_2026-06-30_10-00-00.log");
        r["rpt"].Should().EndWith(".RPT");
        r["adm"].Should().EndWith(".ADM");
        r["console"].Should().EndWith("server_console.log");
        r["client"].Should().EndWith("script_2026-06-30_12-00-00.log");
    }

    [Fact]
    public void Missing_console_log_resolves_to_null_not_a_throw()
    {
        var server = Tmp();   // no server_console.log written
        var r = LogResolver.Resolve(server, Tmp());
        r.Should().ContainKey("console");
        r["console"].Should().BeNull();
    }
}
