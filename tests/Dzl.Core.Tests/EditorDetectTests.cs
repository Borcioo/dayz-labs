using Dzl.Core.Env;
using FluentAssertions;

public class EditorDetectTests
{
    [Fact]
    public void FindOnPath_finds_a_cmd_or_exe_across_path_dirs()
    {
        var a = Directory.CreateTempSubdirectory().FullName;
        var b = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(b, "cursor.cmd"), "");
        var path = string.Join(Path.PathSeparator, a, b);

        EditorDetect.FindOnPath("cursor", path).Should().Be(Path.Combine(b, "cursor.cmd"));
        EditorDetect.FindOnPath("code", path).Should().BeNull();   // not present in either dir
    }

    [Fact]
    public void FindOnPath_null_on_empty_path()
        => EditorDetect.FindOnPath("cursor", "").Should().BeNull();
}
