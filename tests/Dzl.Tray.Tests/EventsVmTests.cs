using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="EventsVm"/> (Economy "Events" tab): load/filter/selection-detail, add/rename
/// (+undo)/remove, scalar persist + validation, and child add. Drives the real VM over a temp mission.</summary>
public class EventsVmTests
{
    private const string Fixture = """
        <events>
          <event name="AmbientA"><nominal>5</nominal><min>1</min><max>9</max><active>1</active></event>
          <event name="AmbientB"><nominal>2</nominal></event>
        </events>
        """;

    private static EventsVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("db/events.xml", Fixture));
        var vm = new EventsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static EventsVm Reloaded(string cfg)
    {
        var vm = new EventsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_events_and_selecting_loads_the_detail_pane()
    {
        var vm = Load(out _);
        vm.Events.Select(e => e.Name).Should().BeEquivalentTo("AmbientA", "AmbientB");

        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientA");
        vm.DetailNominal.Should().Be("5");
        vm.DetailMax.Should().Be("9");
        vm.DetailActive.Should().BeTrue();
    }

    [Fact]
    public void Filter_narrows_the_master_list()
    {
        var vm = Load(out _);
        vm.Filter = "ambientb";
        vm.Events.Select(e => e.Name).Should().ContainSingle().Which.Should().Be("AmbientB");
    }

    [Fact]
    public void AddEvent_then_rename_then_undo_round_trip_on_disk()
    {
        var vm = Load(out var cfg);

        vm.NewEventName = "AmbientC";
        vm.AddEvent();
        Reloaded(cfg).Events.Select(e => e.Name).Should().Contain("AmbientC");

        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientC");
        vm.RenameSelectedEvent("AmbientC2");
        Reloaded(cfg).Events.Select(e => e.Name).Should().Contain("AmbientC2").And.NotContain("AmbientC");

        vm.UndoCommand.Execute(null);   // undo the rename
        Reloaded(cfg).Events.Select(e => e.Name).Should().Contain("AmbientC").And.NotContain("AmbientC2");
    }

    [Fact]
    public void RemoveSelectedEvent_deletes_it()
    {
        var vm = Load(out var cfg);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientB");
        vm.RemoveSelectedEvent();   // confirm => true
        Reloaded(cfg).Events.Select(e => e.Name).Should().NotContain("AmbientB");
    }

    [Fact]
    public void SaveScalar_persists_a_valid_value_and_rejects_a_bad_one()
    {
        var vm = Load(out var cfg);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientA");

        vm.SaveScalar("nominal", "42");
        Reloaded(cfg).Events.Single(e => e.Name == "AmbientA").Nominal.Should().Be(42);

        vm.SaveScalar("nominal", "abc");
        vm.Status.Should().StartWith("✗", "a non-integer scalar is rejected");
    }

    [Fact]
    public void AddChild_appends_a_child_to_the_selected_event()
    {
        var vm = Load(out var cfg);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientA");

        vm.NewChildType = "Animal_Wolf";
        vm.NewChildMin = "1";
        vm.NewChildMax = "3";
        vm.AddChild();

        var reloaded = Reloaded(cfg);
        reloaded.SelectedEvent = reloaded.Events.Single(e => e.Name == "AmbientA");
        reloaded.Children.Select(c => c.Type).Should().Contain("Animal_Wolf");
    }

    [Fact]
    public void SaveScalar_warns_when_min_exceeds_max()
    {
        var vm = Load(out _);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientA");

        vm.DetailMax = "2"; vm.SaveScalar("max", "2");
        vm.DetailMin = "9"; vm.SaveScalar("min", "9");

        vm.Status.Should().StartWith("⚠", "min > max is surfaced live, matching the validator");
    }

    [Fact]
    public void Selecting_syncs_the_inline_rename_box_and_HasSelection()
    {
        var vm = Load(out _);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientA");
        vm.HasSelection.Should().BeTrue();
        vm.RenameText.Should().Be("AmbientA", "the inline-rename box mirrors the selected event's name");
    }

    [Fact]
    public void CommitRename_renames_via_the_inline_box_and_keeps_the_selection()
    {
        var vm = Load(out var cfg);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientA");

        vm.RenameText = "AmbientA_renamed";
        vm.CommitRename();

        Reloaded(cfg).Events.Select(e => e.Name).Should().Contain("AmbientA_renamed").And.NotContain("AmbientA");
        vm.SelectedEvent!.Name.Should().Be("AmbientA_renamed");
    }

    [Fact]
    public void CommitRename_is_a_noop_when_the_name_is_unchanged()
    {
        var vm = Load(out var cfg);
        vm.SelectedEvent = vm.Events.Single(e => e.Name == "AmbientB");

        vm.CommitRename();   // RenameText still "AmbientB" — nothing to do

        Reloaded(cfg).Events.Select(e => e.Name).Should().BeEquivalentTo("AmbientA", "AmbientB");
    }

    [Fact]
    public void AddEvent_seeds_a_position_that_matches_the_editor_combo()
    {
        var vm = Load(out _);
        vm.NewEventName = "NewEv";
        vm.AddEvent();

        vm.SelectedEvent!.Name.Should().Be("NewEv");
        // The Position combo offers only fixed/player; the seeded default must be one of them (was "random").
        vm.DetailPosition.Should().BeOneOf("fixed", "player");
    }

    [Fact]
    public void Master_row_exposes_every_flag_for_the_compact_flag_column()
    {
        const string xml = """
            <events>
              <event name="WithFlags"><nominal>1</nominal><active>1</active>
                <flags deletable="1" init_random="0" remove_damaged="1" /></event>
            </events>
            """;
        var cfg = CeScaffold.Mission(("db/events.xml", xml));
        var vm = new EventsVm(cfg, _ => true);
        vm.Reload();

        var row = vm.Events.Single(e => e.Name == "WithFlags");
        row.Active.Should().BeTrue();
        row.Deletable.Should().BeTrue();
        row.InitRandom.Should().BeFalse();
        row.RemoveDamaged.Should().BeTrue();
    }
}
