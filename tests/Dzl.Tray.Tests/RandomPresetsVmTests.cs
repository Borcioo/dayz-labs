using Dzl.Core.Economy;
using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="RandomPresetsVm"/> (Economy "Random Presets" tab): load/filter/state-filter,
/// add/duplicate-guard, enable-disable toggle, item add, edit-card apply (rename + chance), classname
/// autocomplete, and select-by-finding. Drives the real VM over a temp mission.</summary>
public class RandomPresetsVmTests
{
    private const string Presets = """
        <randompresets>
          <cargo chance="0.15" name="foodHermit"><item name="TunaCan" chance="0.10"/></cargo>
          <attachments chance="0.10" name="optics"><item name="ACOGOptic" chance="1.00"/></attachments>
        </randompresets>
        """;

    private const string Types = """
        <types>
          <type name="TunaCan"><nominal>10</nominal></type>
          <type name="TacticalBacon"><nominal>5</nominal></type>
        </types>
        """;

    private static RandomPresetsVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgrandompresets.xml", Presets), ("db/types.xml", Types));
        var vm = new RandomPresetsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static RandomPresetsVm Reloaded(string cfg)
    {
        var vm = new RandomPresetsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_both_kinds()
    {
        var vm = Load(out _);
        vm.Presets.Select(p => p.Name).Should().BeEquivalentTo("foodHermit", "optics");
    }

    [Fact]
    public void Filter_narrows_by_name()
    {
        var vm = Load(out _);
        vm.Filter = "food";
        vm.Presets.Select(p => p.Name).Should().ContainSingle().Which.Should().Be("foodHermit");
    }

    [Fact]
    public void Filter_also_matches_item_classnames_inside_presets()
    {
        var vm = Load(out _);
        vm.Filter = "tuna";   // TunaCan is an item inside foodHermit, not a preset name
        vm.Presets.Select(p => p.Name).Should().ContainSingle().Which.Should().Be("foodHermit");
    }

    [Fact]
    public void Filter_ranks_name_matches_above_item_only_matches()
    {
        const string presets = """
            <randompresets>
              <cargo chance="0.1" name="ammoBox"><item name="X" chance="1"/></cargo>
              <cargo chance="0.1" name="ammoStash"><item name="Y" chance="1"/></cargo>
              <cargo chance="0.1" name="foodCrate"><item name="ammoSnack" chance="1"/></cargo>
            </randompresets>
            """;
        var cfg = CeScaffold.Mission(("cfgrandompresets.xml", presets));
        var vm = new RandomPresetsVm(cfg, _ => true);
        vm.Reload();

        vm.Filter = "ammo";
        // ammoBox + ammoStash match by NAME; foodCrate matches only via its item (ammoSnack) → ranked last.
        vm.Presets.Select(p => p.Name).Should().Equal("ammoBox", "ammoStash", "foodCrate");
    }

    [Fact]
    public void AddPreset_adds_and_rejects_a_duplicate_name()
    {
        var vm = Load(out var cfg);

        vm.NewPresetName = "ammoStash";
        vm.NewPresetIsCargo = true;
        vm.NewPresetChanceValue = 0.25;
        vm.AddPreset();
        Reloaded(cfg).Presets.Select(p => p.Name).Should().Contain("ammoStash");

        vm.NewPresetName = "foodHermit";   // already a cargo preset
        vm.AddPreset();
        vm.Status.Should().StartWith("✗", "a duplicate kind+name must be rejected");
    }

    [Fact]
    public void ToggleDisabled_round_trips_and_state_filter_isolates_it()
    {
        var vm = Load(out var cfg);
        var food = vm.Presets.Single(p => p.Name == "foodHermit");

        vm.ToggleDisabled(food);

        var reloaded = Reloaded(cfg);
        reloaded.Presets.Single(p => p.Name == "foodHermit").IsDisabled.Should().BeTrue();

        reloaded.StateFilterIndex = 2;   // disabled only
        reloaded.Presets.Select(p => p.Name).Should().ContainSingle().Which.Should().Be("foodHermit");

        reloaded.StateFilterIndex = 1;   // enabled only
        reloaded.Presets.Select(p => p.Name).Should().NotContain("foodHermit");
    }

    [Fact]
    public void AddItem_appends_to_the_selected_preset()
    {
        var vm = Load(out var cfg);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "foodHermit");

        vm.NewItemName = "Pear";
        vm.NewItemChanceValue = 0.05;
        vm.AddItem();

        var reloaded = Reloaded(cfg);
        reloaded.SelectedPreset = reloaded.Presets.Single(p => p.Name == "foodHermit");
        reloaded.Items.Select(i => i.Name).Should().Contain("Pear");
    }

    [Fact]
    public void ApplyEdits_renames_and_changes_chance()
    {
        var vm = Load(out var cfg);
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "foodHermit");

        vm.EditName = "foodHermit2";
        vm.EditChanceValue = 0.5;
        vm.ApplyEdits();

        var reloaded = Reloaded(cfg);
        var moved = reloaded.Presets.Single(p => p.Name == "foodHermit2");
        moved.Chance.Should().BeApproximately(0.5, 1e-9);
        reloaded.Presets.Should().NotContain(p => p.Name == "foodHermit");
    }

    [Fact]
    public void Undo_of_a_rename_reselects_the_preset_not_the_first_row()
    {
        var vm = Load(out _);   // foodHermit (cargo), optics (attachments)
        vm.SelectedPreset = vm.Presets.Single(p => p.Name == "optics");

        vm.EditName = "zzzOptics";   // would re-sort to the end of the list
        vm.ApplyEdits();
        vm.SelectedPreset!.Name.Should().Be("zzzOptics");

        vm.UndoCommand.Execute(null);
        vm.SelectedPreset!.Name.Should().Be("optics",
            "undo reselects the renamed-back preset via its selection token, not the first row");
    }

    [Fact]
    public void TypeNames_pool_is_loaded_for_classname_autocomplete()
    {
        // The VM exposes the full classname pool; the reusable AutoSuggestBox does the filtering (covered by
        // AutoSuggestTests). Here we just confirm the pool is populated from types.xml on load.
        var vm = Load(out _);
        vm.TypeNames.Should().Contain("TunaCan").And.Contain("TacticalBacon");
    }

    [Fact]
    public void SelectByEntry_clears_the_filter_so_the_target_is_selectable()
    {
        var vm = Load(out _);
        vm.Filter = "zzz";                 // hides everything
        vm.Presets.Should().BeEmpty();

        vm.SelectByEntry("optics");

        vm.Filter.Should().BeEmpty("SelectByEntry unfilters so the target row is visible");
        vm.SelectedPreset!.Name.Should().Be("optics");
    }

    [Fact]
    public void DisableUnusedPresets_disables_only_presets_no_spawnabletype_references()
    {
        const string presets = """
            <randompresets>
              <cargo chance="0.1" name="usedCargo"><item name="A" chance="1"/></cargo>
              <cargo chance="0.1" name="deadCargo"><item name="B" chance="1"/></cargo>
              <attachments chance="0.1" name="usedAtt"><item name="C" chance="1"/></attachments>
              <attachments chance="0.1" name="deadAtt"><item name="D" chance="1"/></attachments>
            </randompresets>
            """;
        // A type that references usedCargo (cargo) + usedAtt (attachments). The "dead" presets of each kind
        // are referenced by nothing.
        const string spawn = """
            <spawnabletypes>
              <type name="X"><cargo preset="usedCargo"/><attachments preset="usedAtt"/></type>
            </spawnabletypes>
            """;
        var cfg = CeScaffold.Mission(("cfgrandompresets.xml", presets), ("cfgspawnabletypes.xml", spawn));
        var vm = new RandomPresetsVm(cfg, _ => true);   // confirm => yes
        vm.Reload();

        vm.DisableUnusedPresets();

        var reloaded = Reloaded(cfg);
        reloaded.Presets.Single(p => p.Name == "deadCargo").IsDisabled.Should().BeTrue("no type references it");
        reloaded.Presets.Single(p => p.Name == "deadAtt").IsDisabled.Should().BeTrue("no type references it");
        reloaded.Presets.Single(p => p.Name == "usedCargo").IsDisabled.Should().BeFalse("a cargo block references it");
        reloaded.Presets.Single(p => p.Name == "usedAtt").IsDisabled.Should().BeFalse("an attachments block references it");
    }
}
