using Dzl.Core.App;
using Dzl.Core.Economy;
using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="SpawnableTypesVm"/> (Economy "Spawnable Types" tab): load/filter/selection,
/// rename + undo, add/remove, and the damage-field validation. Drives the real VM over a temp mission.</summary>
public class SpawnableTypesVmTests
{
    private const string Fixture = """
        <spawnabletypes>
          <type name="Alpha"/>
          <type name="Beta"/>
        </spawnabletypes>
        """;

    private static SpawnableTypesVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgspawnabletypes.xml", Fixture));
        var vm = new SpawnableTypesVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_types_sorted_and_selects_the_first()
    {
        var vm = Load(out _);
        vm.Types.Select(t => t.Name).Should().Equal("Alpha", "Beta");
        vm.SelectedType!.Name.Should().Be("Alpha");
    }

    [Fact]
    public void Filter_narrows_the_master_list()
    {
        var vm = Load(out _);
        vm.Filter = "bet";
        vm.ApplyFilterNow();   // the live filter is debounced; apply immediately for the assertion
        vm.Types.Select(t => t.Name).Should().ContainSingle().Which.Should().Be("Beta");
    }

    [Fact]
    public void RenameSelectedType_persists_and_undo_restores()
    {
        var vm = Load(out var cfg);
        vm.SelectedType = vm.Types.Single(t => t.Name == "Alpha");

        vm.RenameSelectedType("Gamma");
        Reloaded(cfg).Types.Select(t => t.Name).Should().Equal("Beta", "Gamma");

        vm.UndoCommand.Execute(null);
        Reloaded(cfg).Types.Select(t => t.Name).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void AddType_then_RemoveSelectedType_round_trip_on_disk()
    {
        var vm = Load(out var cfg);

        vm.NewTypeName = "Delta";
        vm.AddType();
        Reloaded(cfg).Types.Select(t => t.Name).Should().Contain("Delta");

        var vm2 = Reloaded(cfg);
        vm2.SelectedType = vm2.Types.Single(t => t.Name == "Delta");
        vm2.RemoveSelectedType();   // confirm => true
        Reloaded(cfg).Types.Select(t => t.Name).Should().NotContain("Delta");
    }

    [Fact]
    public void SaveHoarder_round_trips_through_the_file()
    {
        var vm = Load(out var cfg);
        vm.SelectedType = vm.Types.Single(t => t.Name == "Alpha");

        vm.SelectedHoarder = true;
        vm.SaveHoarder();

        var reloaded = Reloaded(cfg);
        reloaded.Types.Single(t => t.Name == "Alpha").Hoarder.Should().BeTrue();
    }

    [Fact]
    public void SaveDamage_rejects_out_of_range_values()
    {
        var vm = Load(out _);
        vm.SelectedType = vm.Types.First();

        vm.DamageMin = "2";   // must be empty or 0..1
        vm.SaveDamage();
        vm.Status.Should().StartWith("✗");
    }

    [Fact]
    public void Reload_populates_classname_suggestions_from_types_xml()
    {
        var cfg = CeScaffold.Mission(
            ("cfgspawnabletypes.xml", Fixture),
            ("db/types.xml", "<types><type name=\"Nail\"/><type name=\"Plank\"/></types>"));
        var vm = new SpawnableTypesVm(cfg, _ => true);
        vm.Reload();

        vm.TypeNames.Should().Contain(new[] { "Nail", "Plank" });
    }

    [Fact]
    public void Chance_block_offers_classname_suggestions_and_AddItem_persists()
    {
        var cfg = CeScaffold.Mission(
            ("cfgspawnabletypes.xml", Fixture),
            ("db/types.xml", "<types><type name=\"Nail\"/></types>"));
        var vm = new SpawnableTypesVm(cfg, _ => true);
        vm.Reload();
        vm.SelectedType = vm.Types.Single(t => t.Name == "Alpha");

        vm.AddChanceBlock(isAttachments: false);
        var block = vm.CargoBlocks.Single();

        block.ItemPool.Should().Contain("Nail", "the block exposes the types.xml classname pool to the AutoSuggestBox");

        vm.AddItem(block, "Nail", "0.5");

        var reloaded = Reloaded(cfg);
        reloaded.SelectedType = reloaded.Types.Single(t => t.Name == "Alpha");
        reloaded.CargoBlocks.Single().Items.Select(i => i.Name).Should().Contain("Nail");
    }

    [Fact]
    public void CommitRename_renames_the_selected_type_via_the_inline_box()
    {
        var vm = Load(out var cfg);
        vm.SelectedType = vm.Types.Single(t => t.Name == "Alpha");
        vm.RenameText.Should().Be("Alpha", "the inline rename box syncs to the selected type");

        vm.RenameText = "Gamma";
        vm.CommitRename();

        Reloaded(cfg).Types.Select(t => t.Name).Should().Contain("Gamma").And.NotContain("Alpha");
    }

    [Fact]
    public void RefreshReferences_picks_up_a_preset_rename_from_another_tab()
    {
        var cfg = CeScaffold.Mission(
            ("cfgspawnabletypes.xml", Fixture),
            ("cfgrandompresets.xml", "<randompresets><cargo chance=\"0.1\" name=\"foo\"/></randompresets>"));
        var vm = new SpawnableTypesVm(cfg, _ => true);
        vm.Reload();
        vm.CargoPresetNames.Should().Contain("foo");

        // Simulate the Random Presets tab renaming the preset on disk.
        new RandomPresetsService(cfg).RenamePreset(PresetKind.Cargo, "foo", "bar").ok.Should().BeTrue();

        vm.RefreshReferences();   // what tab activation now does — refresh pools without reloading the model
        vm.CargoPresetNames.Should().Contain("bar").And.NotContain("foo");
    }

    [Fact]
    public void Filter_matches_preset_names_and_item_classnames_not_just_the_type_name()
    {
        const string xml = """
            <spawnabletypes>
              <type name="AK101">
                <cargo preset="ammoArmy"/>
                <attachments chance="0.3"><item name="KashtanOptic" chance="0.5"/></attachments>
              </type>
              <type name="Apple"/>
            </spawnabletypes>
            """;
        var cfg = CeScaffold.Mission(("cfgspawnabletypes.xml", xml));
        var vm = new SpawnableTypesVm(cfg, _ => true);
        vm.Reload();

        vm.Filter = "ammo"; vm.ApplyFilterNow();   // matches the referenced preset name
        vm.Types.Select(t => t.Name).Should().ContainSingle().Which.Should().Be("AK101");

        vm.Filter = "optic"; vm.ApplyFilterNow();  // matches an inline item classname
        vm.Types.Select(t => t.Name).Should().ContainSingle().Which.Should().Be("AK101");

        vm.Filter = "appl"; vm.ApplyFilterNow();   // still matches by the type's own name
        vm.Types.Select(t => t.Name).Should().ContainSingle().Which.Should().Be("Apple");
    }

    [Fact]
    public void SelectByEntry_selects_the_type_without_filtering_the_list()
    {
        var vm = Load(out _);   // Alpha, Beta
        vm.SelectByEntry("Beta");

        vm.SelectedType!.Name.Should().Be("Beta");
        vm.Filter.Should().BeEmpty("selecting an entry must not set a filter");
        vm.Types.Should().HaveCount(2, "the list stays full, not narrowed to the entry");
    }

    private static SpawnableTypesVm Reloaded(string cfg)
    {
        var vm = new SpawnableTypesVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }
}
