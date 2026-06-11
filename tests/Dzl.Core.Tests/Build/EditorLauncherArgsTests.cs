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
    public void VsCode_family_opens_the_project_folder_as_workspace_plus_goto()
    {
        EditorLauncher.FileArgs(@"C:\x\Code.exe", @"D:\mods\Foo\config.cpp", 42, @"D:\mods\Foo")
            .Should().Equal(@"D:\mods\Foo", "--goto", @"D:\mods\Foo\config.cpp:42");
    }

    [Fact]
    public void Unknown_editor_gets_plain_file_path_even_with_folder()
    {
        EditorLauncher.FileArgs(@"C:\tools\notepad++.exe", @"D:\mods\Foo\config.cpp", 42, @"D:\mods\Foo")
            .Should().Equal(@"D:\mods\Foo\config.cpp");
    }

    [Fact]
    public void No_line_means_folder_plus_file_for_vscode()
    {
        EditorLauncher.FileArgs(@"C:\x\Code.exe", @"D:\f.cpp", 0)
            .Should().Equal(@"D:\f.cpp");
        EditorLauncher.FileArgs(@"C:\x\Code.exe", @"D:\mods\Foo\f.cpp", 0, @"D:\mods\Foo")
            .Should().Equal(@"D:\mods\Foo", @"D:\mods\Foo\f.cpp");
    }
}
