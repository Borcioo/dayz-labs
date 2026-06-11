using Dzl.Core.Tools;
using FluentAssertions;
using Xunit;

public class EditorLauncherArgsTests
{
    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\cursor\Cursor.exe")]
    [InlineData(@"C:\Program Files\Microsoft VS Code\Code.exe")]
    [InlineData(@"C:\path\code-insiders.cmd")]
    public void VsCode_family_gets_goto_file_line(string editor)
    {
        EditorLauncher.FileArgs(editor, @"D:\mods\Foo\config.cpp", 42)
            .Should().Equal("--goto", @"D:\mods\Foo\config.cpp:42");
    }

    [Fact]
    public void Unknown_editor_gets_plain_file_path()
    {
        EditorLauncher.FileArgs(@"C:\tools\notepad++.exe", @"D:\mods\Foo\config.cpp", 42)
            .Should().Equal(@"D:\mods\Foo\config.cpp");
    }

    [Fact]
    public void No_line_means_plain_file_path_even_for_vscode()
    {
        EditorLauncher.FileArgs(@"C:\x\Code.exe", @"D:\f.cpp", 0)
            .Should().Equal(@"D:\f.cpp");
    }
}
