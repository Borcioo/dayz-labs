using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="EventGroupsVm"/> (Events "Event Groups" tab = cfgeventgroups.xml): group
/// list + filter, the selected group's children, add/remove of groups + children, and child edit/validation.</summary>
public class EventGroupsVmTests
{
    private const string Fixture = """
        <eventgroupdef>
          <group name="Train_Cherno">
            <child type="Wreck_A" deloot="0" lootmax="3" lootmin="1" x="0" z="0" a="78" y="1.9"/>
          </group>
          <group name="Heli_Crash"/>
        </eventgroupdef>
        """;

    private static EventGroupsVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgeventgroups.xml", Fixture));
        var vm = new EventGroupsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static EventGroupsVm Reloaded(string cfg)
    {
        var vm = new EventGroupsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_groups_and_loads_children()
    {
        var vm = Load(out _);
        vm.Groups.Select(g => g.Name).Should().BeEquivalentTo("Train_Cherno", "Heli_Crash");
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "Train_Cherno");
        vm.Children.Should().ContainSingle().Which.Type.Should().Be("Wreck_A");
    }

    [Fact]
    public void AddChild_then_edit_persists()
    {
        var vm = Load(out var cfg);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "Heli_Crash");
        vm.NewChildType = "Land_Wreck_Heli";
        vm.AddChild();

        var vm2 = Reloaded(cfg);
        vm2.SelectedGroup = vm2.Groups.Single(g => g.Name == "Heli_Crash");
        var child = vm2.Children.Single();
        child.Type.Should().Be("Land_Wreck_Heli");
        child.XText = "10";
        child.LootMaxText = "4";
        child.Commit();

        var reloaded = Reloaded(cfg);
        reloaded.SelectedGroup = reloaded.Groups.Single(g => g.Name == "Heli_Crash");
        reloaded.Children.Single().XText.Should().Be("10");
        reloaded.Children.Single().LootMaxText.Should().Be("4");
    }

    [Fact]
    public void Editing_a_child_rejects_a_non_numeric_offset()
    {
        var vm = Load(out var cfg);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "Train_Cherno");
        var c = vm.Children.Single();
        c.XText = "abc";
        c.Commit();
        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Groups.Single(g => g.Name == "Train_Cherno");   // unchanged file
    }

    [Fact]
    public void RemoveSelectedGroup_deletes_it()
    {
        var vm = Load(out var cfg);
        vm.SelectedGroup = vm.Groups.Single(g => g.Name == "Train_Cherno");
        vm.RemoveSelectedGroup();   // confirm => true
        Reloaded(cfg).Groups.Select(g => g.Name).Should().NotContain("Train_Cherno");
    }
}
