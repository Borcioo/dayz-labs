using Dzl.Core.Logs;
using FluentAssertions;

public class LogSyntaxTests
{
    private static string Sub(string line, LogSpan s) => line.Substring(s.Start, s.Length);
    private static IReadOnlyList<LogSpan> Tok(string line) => LogSyntax.Tokenize(line);

    [Fact]
    public void Tokenizes_an_rpt_error_line_into_timestamp_subsystem_severity()
    {
        const string line = "10:35:24.380 RESOURCES (E): CreateTextureFromImage(perlin.edds) failed";
        var t = Tok(line);

        t.Should().Contain(s => s.Token == LogToken.Timestamp && Sub(line, s) == "10:35:24.380");
        t.Should().Contain(s => s.Token == LogToken.Subsystem && Sub(line, s) == "RESOURCES");
        t.Should().Contain(s => s.Token == LogToken.Error && Sub(line, s) == "(E)");
    }

    [Fact]
    public void Tokenizes_a_warning_marker()
    {
        const string line = "10:35:24.161 ENGINE    (W): Enfusion setting file load failed";
        Tok(line).Should().Contain(s => s.Token == LogToken.Warning && Sub(line, s) == "(W)");
    }

    [Fact]
    public void Marks_a_relative_script_file_reference_with_its_line_number()
    {
        const string line = " SCRIPT    (E):    GetPlugin() scripts/4_World/plugins/pluginmanager.c : 339";
        var t = Tok(line);
        t.Should().Contain(s => s.Token == LogToken.FileRef &&
                                Sub(line, s) == "scripts/4_World/plugins/pluginmanager.c : 339");
    }

    [Fact]
    public void FileRefAt_returns_relative_path_and_line_for_a_column_inside_the_ref()
    {
        const string line = " SCRIPT    (E):    GetPlugin() scripts/4_World/plugins/pluginmanager.c : 339";
        var col = line.IndexOf("pluginmanager", StringComparison.Ordinal);
        var hit = LogSyntax.FileRefAt(line, col);
        hit.Should().NotBeNull();
        hit!.Value.Path.Should().Be("scripts/4_World/plugins/pluginmanager.c");
        hit.Value.Line.Should().Be(339);
    }

    [Fact]
    public void FileRefAt_handles_an_absolute_windows_path()
    {
        const string line = "CreateCustomMission() D:\\DayzProjects\\servers\\xcvbcvb\\mpmissions\\dayzOffline.chernarusplus\\init.c : 97";
        var col = line.IndexOf("init.c", StringComparison.Ordinal);
        var hit = LogSyntax.FileRefAt(line, col);
        hit.Should().NotBeNull();
        hit!.Value.Path.Should().Be("D:\\DayzProjects\\servers\\xcvbcvb\\mpmissions\\dayzOffline.chernarusplus\\init.c");
        hit.Value.Line.Should().Be(97);
    }

    [Fact]
    public void FileRefAt_returns_null_off_a_reference()
    {
        const string line = "10:35:24.380 RESOURCES (E): nothing clickable here";
        LogSyntax.FileRefAt(line, 30).Should().BeNull();
    }

    [Fact]
    public void Tokenizes_quoted_player_name_in_an_adm_line()
    {
        const string line = "10:35:24 | Player \"DevMacie\" (id=9aNxyz pos=<13082, 8060, 1.8>) is connected";
        var t = Tok(line);
        t.Should().Contain(s => s.Token == LogToken.Quoted && Sub(line, s) == "\"DevMacie\"");
        t.Should().Contain(s => s.Token == LogToken.Pos && Sub(line, s) == "pos=<13082, 8060, 1.8>");
    }

    [Fact]
    public void Tokenizes_console_bracket_tags_and_success_keyword()
    {
        const string line = "10:35:41 [CE][offlineDB] :: Conversion successfully completed.";
        var t = Tok(line);
        t.Should().Contain(s => s.Token == LogToken.Subsystem && Sub(line, s) == "[CE]");
        t.Should().Contain(s => s.Token == LogToken.Success && Sub(line, s) == "successfully");
    }

    [Fact]
    public void Spans_are_sorted_and_non_overlapping()
    {
        const string line = "10:35:24.380 RESOURCES (E): GetPlugin() scripts/foo.c : 12 'x'";
        var t = Tok(line);
        for (var i = 1; i < t.Count; i++)
            t[i].Start.Should().BeGreaterThanOrEqualTo(t[i - 1].Start + t[i - 1].Length);
    }
}
