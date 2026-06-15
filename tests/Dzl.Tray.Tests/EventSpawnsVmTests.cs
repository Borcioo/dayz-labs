using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="EventSpawnsVm"/> (Events "Event Spawns" tab = cfgeventspawns.xml): event
/// list + filter, the selected event's positions, and add/remove of events + positions.</summary>
public class EventSpawnsVmTests
{
    private const string Fixture = """
        <eventposdef>
          <event name="VehicleSedan"><pos x="1" z="2" a="3"/></event>
          <event name="StaticHeliCrash"/>
        </eventposdef>
        """;

    private static EventSpawnsVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgeventspawns.xml", Fixture));
        var vm = new EventSpawnsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static EventSpawnsVm Reloaded(string cfg)
    {
        var vm = new EventSpawnsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_events_and_filter_narrows()
    {
        var vm = Load(out _);
        vm.Events.Select(e => e.Name).Should().BeEquivalentTo("VehicleSedan", "StaticHeliCrash");
        vm.Filter = "heli";
        vm.Events.Select(e => e.Name).Should().ContainSingle().Which.Should().Be("StaticHeliCrash");
    }

    [Fact]
    public void Selecting_an_event_loads_its_positions()
    {
        var vm = Load(out _);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "VehicleSedan");
        vm.Positions.Should().ContainSingle();
        vm.Positions[0].XText.Should().Be("1");
    }

    [Fact]
    public void AddEvent_and_AddPos_persist()
    {
        var vm = Load(out var cfg);
        vm.NewEventName = "ContaminatedArea";
        vm.AddEvent();
        Reloaded(cfg).Events.Select(e => e.Name).Should().Contain("ContaminatedArea");

        var vm2 = Reloaded(cfg);
        vm2.SelectedEvent = vm2.Events.Single(e => e.Name == "StaticHeliCrash");
        vm2.AddPos("500", "600", "45");
        var reloaded = Reloaded(cfg);
        reloaded.SelectedEvent = reloaded.Events.Single(e => e.Name == "StaticHeliCrash");
        reloaded.Positions.Should().ContainSingle();
    }

    [Fact]
    public void RemoveSelectedEvent_deletes_it()
    {
        var vm = Load(out var cfg);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "VehicleSedan");
        vm.RemoveSelectedEvent();   // confirm => true
        Reloaded(cfg).Events.Select(e => e.Name).Should().NotContain("VehicleSedan");
    }
}
