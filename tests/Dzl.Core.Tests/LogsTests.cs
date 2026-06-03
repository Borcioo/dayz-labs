using Dzl.Core.Logs;
using FluentAssertions;
using Xunit;

public class LogsTests
{
    private static string NewDir() => Directory.CreateTempSubdirectory().FullName;

    private static void Write(string dir, string name, string content, DateTime mtimeUtc)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        File.SetLastWriteTimeUtc(p, mtimeUtc);
    }

    [Fact]
    public void Resolve_picks_newest_per_kind()
    {
        var srv = NewDir();
        var cli = NewDir();
        Write(srv, "script_2026-01-01.log", "old", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(srv, "script_2026-02-01.log", "new", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(srv, "DayZDiag_x64_a.RPT", "rpt", new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        Write(srv, "DayZDiag_x64_a.ADM", "adm", new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc));
        Write(cli, "script_2026-03-01.log", "client", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var r = LogResolver.Resolve(srv, cli);
        Path.GetFileName(r["script"]).Should().Be("script_2026-02-01.log");
        Path.GetFileName(r["rpt"]).Should().Be("DayZDiag_x64_a.RPT");
        Path.GetFileName(r["adm"]).Should().Be("DayZDiag_x64_a.ADM");
        Path.GetFileName(r["client"]).Should().Be("script_2026-03-01.log");
    }

    [Fact]
    public void Resolve_returns_null_keys_when_no_match()
    {
        var r = LogResolver.Resolve(NewDir(), NewDir());
        r.Should().ContainKeys("script", "rpt", "adm", "client");
        r["script"].Should().BeNull();
        r["client"].Should().BeNull();
    }

    [Fact]
    public void LastLines_returns_tail()
    {
        var dir = NewDir();
        var p = Path.Combine(dir, "x.log");
        File.WriteAllLines(p, new[] { "a", "b", "c", "d", "e" });
        LogTail.LastLines(p, 2).Should().Equal("d", "e");
        LogTail.LastLines(p, 99).Should().HaveCount(5);
    }

    [Fact]
    public void LastLines_missing_file_is_empty()
        => LogTail.LastLines(Path.Combine(NewDir(), "nope.log"), 5).Should().BeEmpty();
}
