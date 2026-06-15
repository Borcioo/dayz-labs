using Dzl.Core.App;
using FluentAssertions;

/// <summary>Pure listing of a mission's auto-generated map-data files (mapgroup*/mapcluster*).</summary>
public class MapFilesTests
{
    [Fact]
    public void ListIn_returns_only_map_files_sorted_with_descriptions()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "mapgrouppos.xml"), "<x/>");
        File.WriteAllText(Path.Combine(dir, "mapgroupcluster.xml"), "<x/>");
        File.WriteAllText(Path.Combine(dir, "mapclusterproto.xml"), "<x/>");
        File.WriteAllText(Path.Combine(dir, "types.xml"), "<x/>");   // not a map file

        var files = MapFiles.ListIn(dir);

        files.Select(f => f.Name).Should().Equal("mapclusterproto.xml", "mapgroupcluster.xml", "mapgrouppos.xml");
        files.Single(f => f.Name == "mapgrouppos.xml").Description.Should().Contain("Building");
    }

    [Fact]
    public void ListIn_is_empty_for_null_or_missing_dir()
    {
        MapFiles.ListIn(null).Should().BeEmpty();
        MapFiles.ListIn("Z:\\definitely-not-a-real-mission-dir").Should().BeEmpty();
    }
}
