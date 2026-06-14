using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="PlayerSpawnsVm"/> (Economy "Player Spawns" tab) beyond the param rename/add
/// guards (in TrayCeViewModelTests): category load, group add/rename/remove, and position add.</summary>
public class PlayerSpawnsVmTests
{
    private const string Fixture = """
        <playerspawnpoints>
          <fresh>
            <generator_posbubbles>
              <group name="West"><pos x="1.0" z="2.0"/></group>
            </generator_posbubbles>
          </fresh>
        </playerspawnpoints>
        """;

    private static PlayerSpawnsVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgplayerspawnpoints.xml", Fixture));
        var vm = new PlayerSpawnsVm(cfg, _ => true);
        vm.Reload();
        vm.SelectedCategory = "fresh";
        return vm;
    }

    private static PlayerSpawnsVm Reloaded(string cfg)
    {
        var vm = new PlayerSpawnsVm(cfg, _ => true);
        vm.Reload();
        vm.SelectedCategory = "fresh";
        return vm;
    }

    [Fact]
    public void Reload_surfaces_categories_and_groups()
    {
        var vm = Load(out _);
        vm.Categories.Should().Contain("fresh");
        vm.Groups.Select(g => g.Name).Should().ContainSingle().Which.Should().Be("West");
    }

    [Fact]
    public void AddGroup_adds_to_the_container()
    {
        var vm = Load(out var cfg);
        vm.NewGroupName = "East";
        vm.NewGroupContainer = "generator_posbubbles";
        vm.AddGroup();

        Reloaded(cfg).Groups.Select(g => g.Name).Should().Contain("East");
    }

    [Fact]
    public void RenameSelectedGroup_renames_it()
    {
        var vm = Load(out var cfg);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "West");
        vm.RenameSelectedGroup("FarWest");

        Reloaded(cfg).Groups.Select(g => g.Name).Should().Contain("FarWest").And.NotContain("West");
    }

    [Fact]
    public void RemoveSelectedGroup_deletes_it()
    {
        var vm = Load(out var cfg);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "West");
        vm.RemoveSelectedGroup();   // confirm => true

        Reloaded(cfg).Groups.Should().BeEmpty();
    }

    [Fact]
    public void AddPos_appends_a_position_to_the_selected_group()
    {
        var vm = Load(out var cfg);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "West");
        var before = vm.Positions.Count;

        vm.AddPos("10.5", "20.5");

        vm.Positions.Count.Should().Be(before + 1);
        var reloaded = Reloaded(cfg);
        reloaded.SelectedGroup = reloaded.Groups.Single(g => g.Name == "West");
        reloaded.Positions.Count.Should().Be(before + 1);
    }
}
