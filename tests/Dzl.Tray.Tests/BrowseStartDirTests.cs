using System.IO;
using Dzl.Tray;
using FluentAssertions;

public class BrowseStartDirTests
{
    private static Func<string, bool> Exists(params string[] dirs)
    {
        var set = new HashSet<string>(dirs.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void Existing_folder_field_opens_in_that_folder()
    {
        BrowseStartDir.Resolve(@"D:\srv\alpha\mpmissions\dayzOffline.chernarusplus", isFile: false,
            fallbacks: new[] { @"E:\install" }, exists: Exists(@"D:\srv\alpha\mpmissions\dayzOffline.chernarusplus"))
            .Should().Be(@"D:\srv\alpha\mpmissions\dayzOffline.chernarusplus");
    }

    [Fact]
    public void File_field_opens_in_its_parent_directory()
    {
        BrowseStartDir.Resolve(@"D:\srv\alpha\serverDZ.cfg", isFile: true,
            fallbacks: new[] { @"E:\install" }, exists: Exists(@"D:\srv\alpha"))
            .Should().Be(@"D:\srv\alpha");
    }

    [Fact]
    public void Missing_folder_field_falls_back_to_its_existing_parent()
    {
        BrowseStartDir.Resolve(@"D:\srv\alpha\mpmissions\gone", isFile: false,
            fallbacks: new[] { @"E:\install" }, exists: Exists(@"D:\srv\alpha\mpmissions"))
            .Should().Be(@"D:\srv\alpha\mpmissions");
    }

    [Fact]
    public void Empty_or_nonexistent_field_falls_back_to_first_existing_candidate()
    {
        BrowseStartDir.Resolve("", isFile: false,
            fallbacks: new[] { @"E:\nope", @"E:\install" }, exists: Exists(@"E:\install"))
            .Should().Be(@"E:\install");
    }
}
