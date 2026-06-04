using Dzl.Core.Projects;
using FluentAssertions;

public class ProjectPathsTests
{
    [Theory]
    [InlineData("MyMod", true)]
    [InlineData("Mod_1", true)]
    [InlineData("a", true)]
    [InlineData("1Mod", false)]    // must start with a letter
    [InlineData("my-mod", false)]  // no dashes
    [InlineData("my mod", false)]  // no spaces
    [InlineData("", false)]
    public void Name_validation(string name, bool ok)
        => ProjectPaths.IsValidName(name).Should().Be(ok);

    [Fact]
    public void Name_validation_null_is_false() => ProjectPaths.IsValidName(null).Should().BeFalse();

    [Fact]
    public void Root_falls_back_to_userprofile_when_unset()
        => ProjectPaths.Root("", @"C:\Users\me").Should().Be(@"C:\Users\me\DayZProjects");

    [Fact]
    public void Root_falls_back_when_whitespace()
        => ProjectPaths.Root("   ", @"C:\Users\me").Should().Be(@"C:\Users\me\DayZProjects");

    [Fact]
    public void Root_uses_configured_when_set()
        => ProjectPaths.Root(@"D:\Dev\DayZ", @"C:\Users\me").Should().Be(@"D:\Dev\DayZ");

    [Fact]
    public void Paths_compose_under_root()
    {
        ProjectPaths.ModDir(@"D:\P", "MyMod").Should().Be(@"D:\P\MyMod");
        ProjectPaths.ServersDir(@"D:\P").Should().Be(@"D:\P\servers");
        ProjectPaths.ServerDir(@"D:\P", "chernarus").Should().Be(@"D:\P\servers\chernarus");
        ProjectPaths.WorkDriveLink("MyMod").Should().Be(@"P:\MyMod");
    }
}
