using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for the redesigned <see cref="PlayerSpawnsVm"/> (Economy "Player Spawns" dashboard): category
/// nav-rail with counts, the documented setting fields (numeric + bool, signed allowed) loading from / persisting
/// to the file, "Other" (non-canonical) param preservation, and the groups↔positions master-detail.</summary>
public class PlayerSpawnsVmTests
{
    private const string Fixture = """
        <playerspawnpoints>
          <fresh>
            <spawn_params>
              <min_dist_player>65</min_dist_player>
              <max_dist_player>150</max_dist_player>
            </spawn_params>
            <generator_params>
              <grid_width>200</grid_width>
              <min_steepness>-45</min_steepness>
            </generator_params>
            <group_params>
              <enablegroups>true</enablegroups>
              <counter>2</counter>
            </group_params>
            <generator_posbubbles>
              <group name="West"><pos x="1.0" z="2.0"/></group>
            </generator_posbubbles>
          </fresh>
          <hop>
            <spawn_params><min_dist_player>25</min_dist_player></spawn_params>
          </hop>
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
    public void Reload_surfaces_category_tabs_and_groups()
    {
        var vm = Load(out _);
        vm.CategoryTabs.Select(t => t.Name).Should().Contain(new[] { "fresh", "hop" });
        vm.Groups.Select(g => g.Name).Should().ContainSingle().Which.Should().Be("West");
    }

    [Fact]
    public void Category_tabs_carry_counts_and_official_flag()
    {
        var vm = Load(out _);
        var fresh = vm.CategoryTabs.Single(t => t.Name == "fresh");
        fresh.GroupCount.Should().Be(1);
        fresh.PosCount.Should().Be(1);
        fresh.IsOfficial.Should().BeFalse("fresh is the required, always-relevant category");
        vm.CategoryTabs.Single(t => t.Name == "hop").IsOfficial.Should().BeTrue("hop only applies to official servers");
    }

    [Fact]
    public void Documented_fields_load_from_the_file_including_a_signed_value()
    {
        var vm = Load(out _);
        vm.ScorePlayerMin.Number.Should().Be(65);
        vm.ScorePlayerMax.Number.Should().Be(150);
        vm.GenWidth.Number.Should().Be(200);
        vm.GenSteepMin.Number.Should().Be(-45, "steepness can be negative");
        vm.GrpEnable.Flag.Should().BeTrue();
        vm.GrpCounter.Number.Should().Be(2);
        vm.ScoreInfectedMin.Number.Should().BeNull("a field absent from the file stays empty");
    }

    [Fact]
    public void Switching_category_reloads_that_categorys_fields()
    {
        var vm = Load(out _);
        vm.ScorePlayerMin.Number.Should().Be(65);
        vm.SelectedCategory = "hop";
        vm.ScorePlayerMin.Number.Should().Be(25, "hop has its own spawn_params");
    }

    [Fact]
    public void Editing_a_numeric_field_persists()
    {
        var vm = Load(out var cfg);
        vm.GenWidth.Number = 250;
        Reloaded(cfg).GenWidth.Number.Should().Be(250);
    }

    [Fact]
    public void Editing_a_signed_field_persists_the_negative_value()
    {
        var vm = Load(out var cfg);
        vm.GenSteepMin.Number = -30;
        Reloaded(cfg).GenSteepMin.Number.Should().Be(-30);
    }

    [Fact]
    public void Toggling_a_bool_field_persists()
    {
        var vm = Load(out var cfg);
        vm.GrpEnable.Flag = false;
        Reloaded(cfg).GrpEnable.Flag.Should().BeFalse();
    }

    [Fact]
    public void Non_canonical_keys_surface_as_other_params()
    {
        var vm = Load(out var cfg);
        // grid_width under spawn_params is non-canonical there (it's a generator_params field) → "Other".
        vm.AddOtherParam("spawn_params", "tries", "3");
        var reloaded = Reloaded(cfg);
        reloaded.HasOtherParams.Should().BeTrue();
        reloaded.OtherParams.Select(p => p.Name).Should().Contain("tries");
    }

    [Fact]
    public void AddOtherParam_rejects_a_non_numeric_value()
    {
        var vm = Load(out var cfg);
        vm.AddOtherParam("spawn_params", "tries", "abc");
        vm.Status.Should().StartWith("✗", "player-spawn param values are numeric");
        Reloaded(cfg).OtherParams.Select(p => p.Name).Should().NotContain("tries");
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
