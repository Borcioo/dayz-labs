using Xunit;
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
    public void Name_validation_64_chars_ok()
        => ProjectPaths.IsValidName("A" + new string('a', 63)).Should().BeTrue();   // 64 chars

    [Fact]
    public void Name_validation_65_chars_rejected()
        => ProjectPaths.IsValidName("A" + new string('a', 64)).Should().BeFalse();  // 65 chars

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
        ProjectPaths.ModsDir(@"D:\P").Should().Be(@"D:\P\mods");
        ProjectPaths.ModDir(@"D:\P", "MyMod").Should().Be(@"D:\P\mods\MyMod");
        ProjectPaths.ModMetaDir(@"D:\P", "MyMod").Should().Be(@"D:\P\mods\MyMod\.dzl");
        ProjectPaths.BuildDir(@"D:\P", "MyMod").Should().Be(@"D:\P\build\@MyMod");
        ProjectPaths.BuildAddonsDir(@"D:\P", "MyMod").Should().Be(@"D:\P\build\@MyMod\Addons");
        ProjectPaths.ServersDir(@"D:\P").Should().Be(@"D:\P\servers");
        ProjectPaths.ServerDir(@"D:\P", "chernarus").Should().Be(@"D:\P\servers\chernarus");
        ProjectPaths.WorkDriveLink("MyMod").Should().Be(@"P:\MyMod");
        ProjectPaths.BuildLink("MyMod").Should().Be(@"P:\Mods\@MyMod");
    }

    [Fact]
    public void Key_paths_resolve_under_keys_dir_with_override()
    {
        ProjectPaths.KeysDir(@"D:\P", null).Should().Be(@"D:\P\keys");
        ProjectPaths.KeysDir(@"D:\P", @"E:\MyKeys").Should().Be(@"E:\MyKeys");
        ProjectPaths.PrivateKey(@"D:\P", null, "Macie").Should().Be(@"D:\P\keys\Macie.biprivatekey");
        ProjectPaths.PublicKey(@"D:\P", null, "Macie").Should().Be(@"D:\P\keys\Macie.bikey");
        ProjectPaths.PublicKey(@"D:\P", @"E:\K", "Macie").Should().Be(@"E:\K\Macie.bikey");
        ProjectPaths.ModKeysDir(@"D:\P", "MyMod").Should().Be(@"D:\P\mods\MyMod\keys");
    }

    [Fact]
    public void Build_area_is_a_single_junction_on_the_work_drive_source()
    {
        ProjectPaths.BuildAreaJunction(@"D:\DayZWorkDrive").Should().Be(@"D:\DayZWorkDrive\Mods");
        ProjectPaths.BuildAreaJunction(null).Should().Be(@"P:\Mods");
    }

    [Fact]
    public void JunctionPath_anchors_on_the_work_drive_source_folder()
        => ProjectPaths.JunctionPath(@"D:\DayZWorkDrive", "MyMod").Should().Be(@"D:\DayZWorkDrive\MyMod");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void JunctionPath_falls_back_to_P_when_source_unknown(string? source)
        => ProjectPaths.JunctionPath(source, "MyMod").Should().Be(@"P:\MyMod");
}
