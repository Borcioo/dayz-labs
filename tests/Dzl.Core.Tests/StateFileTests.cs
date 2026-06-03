using Dzl.Core.Launch;
using FluentAssertions;
using Xunit;

public class StateFileTests
{
    private static string Cfg() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");

    [Fact]
    public void Path_is_next_to_config()
        => StateFile.Path(Path.Combine("x", "config.json")).Replace('\\', '/').Should().EndWith("/.dzl-procs.json");

    [Fact]
    public void Write_raw_read_clear_round_trip()
    {
        var cfg = Cfg();
        StateFile.Write(cfg, "server", 4242, "debug", "cli", "DayZDiag_x64.exe");
        StateFile.ReadRaw(cfg)["server"].Pid.Should().Be(4242);
        StateFile.ReadRaw(cfg)["server"].Source.Should().Be("cli");
        StateFile.Clear(cfg, "server");
        StateFile.ReadRaw(cfg).Should().NotContainKey("server");
    }

    [Fact]
    public void Reconcile_drops_dead_or_wrong_image_keeps_alive()
    {
        var cfg = Cfg();
        StateFile.Write(cfg, "server", 999999, "debug", "cli", "DayZDiag_x64.exe");
        StateFile.ReadLive(cfg, _ => null).Should().NotContainKey("server");           // dead pid
        StateFile.Write(cfg, "server", 4242, "debug", "cli", "DayZDiag_x64.exe");
        StateFile.ReadLive(cfg, _ => "notepad.exe").Should().NotContainKey("server");   // recycled pid, wrong image
        StateFile.Write(cfg, "server", 4242, "debug", "cli", "DayZDiag_x64.exe");
        StateFile.ReadLive(cfg, _ => "DayZDiag_x64.exe").Should().ContainKey("server"); // alive, matching image
    }

    [Fact]
    public void Missing_or_corrupt_file_reads_empty()
    {
        var cfg = Cfg();
        StateFile.ReadRaw(cfg).Should().BeEmpty();
        File.WriteAllText(StateFile.Path(cfg), "{ broken");
        StateFile.ReadRaw(cfg).Should().BeEmpty();
    }
}
