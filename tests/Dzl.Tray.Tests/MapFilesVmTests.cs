using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="MapFilesVm"/> (Economy "Map files" tab): lists the mission's map-data files
/// for the open-externally shortcuts. Plain ObservableObject — no STA needed.</summary>
public class MapFilesVmTests
{
    [Fact]
    public void Reload_lists_the_missions_map_files()
    {
        var cfg = CeScaffold.Mission(
            ("mapgrouppos.xml", "<map/>"),
            ("mapgroupproto.xml", "<map/>"),
            ("db/types.xml", "<types/>"));   // a non-map CE file is ignored

        var vm = new MapFilesVm(cfg);
        vm.Reload();

        vm.HasMission.Should().BeTrue();
        vm.HasFiles.Should().BeTrue();
        vm.Files.Select(f => f.Name).Should().BeEquivalentTo("mapgrouppos.xml", "mapgroupproto.xml");
    }

    [Fact]
    public void Reload_is_empty_when_no_map_files_present()
    {
        var cfg = CeScaffold.Mission(("db/globals.xml", "<variables/>"));
        var vm = new MapFilesVm(cfg);
        vm.Reload();

        vm.HasFiles.Should().BeFalse("the mission has no mapgroup*/mapcluster* files");
    }
}
