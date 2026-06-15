using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="GlobalsVm"/> (Economy "Globals" tab). globals.xml is a CLOSED engine
/// vocabulary, so standard vars are flagged <c>IsKnown</c>: value-editable but with a fixed name + type, not
/// removable (reset-to-default instead). Only a custom/non-standard key is fully editable + removable, and
/// "Add" offers only known-but-missing names. Covers that model plus load/filter and value validation.</summary>
public class GlobalsVmTests
{
    private const string Fixture = """
        <variables>
          <var name="AnimalMaxCount" type="0" value="100"/>
          <var name="ZombieMaxCount" type="0" value="200"/>
          <var name="MyCustomTweak" type="1" value="0.5"/>
        </variables>
        """;

    private static GlobalsVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("db/globals.xml", Fixture));
        var vm = new GlobalsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static GlobalsVm Reloaded(string cfg)
    {
        var vm = new GlobalsVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_vars_and_filter_narrows()
    {
        var vm = Load(out _);
        vm.Rows.Select(r => r.Name).Should().BeEquivalentTo("AnimalMaxCount", "ZombieMaxCount", "MyCustomTweak");

        vm.Filter = "zombie";
        vm.Rows.Select(r => r.Name).Should().ContainSingle().Which.Should().Be("ZombieMaxCount");
    }

    [Fact]
    public void Standard_vars_are_known_custom_keys_are_not()
    {
        var vm = Load(out _);
        vm.Rows.Single(r => r.Name == "AnimalMaxCount").IsKnown.Should().BeTrue();
        vm.Rows.Single(r => r.Name == "MyCustomTweak").IsKnown.Should().BeFalse("it isn't a documented engine global");
    }

    [Fact]
    public void Editing_value_of_a_known_var_persists()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");
        vm.DetailValue = "150";
        vm.SaveDetail();

        var saved = Reloaded(cfg).Rows.Single(r => r.Name == "AnimalMaxCount");
        saved.Value.Should().Be("150");
        saved.Type.Should().Be(0, "the engine type is preserved");
    }

    [Fact]
    public void A_known_vars_type_is_never_changed_even_if_requested()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");   // int
        vm.DetailType = 1;          // user tries to switch to float (UI disables this; guard it anyway)
        vm.DetailValue = "150";
        vm.SaveDetail();

        Reloaded(cfg).Rows.Single(r => r.Name == "AnimalMaxCount").Type
            .Should().Be(0, "a standard engine variable's type is fixed");
    }

    [Fact]
    public void SaveDetail_rejects_a_non_numeric_value()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");
        vm.DetailValue = "potato";
        vm.SaveDetail();

        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Rows.Single(r => r.Name == "AnimalMaxCount").Value.Should().Be("100");
    }

    [Fact]
    public void A_known_var_cannot_be_renamed()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");
        vm.RenameText = "Foo";
        vm.CommitRename();

        vm.Status.Should().StartWith("✗", "the name is the engine key — fixed for standard vars");
        Reloaded(cfg).Rows.Select(r => r.Name).Should().Contain("AnimalMaxCount").And.NotContain("Foo");
    }

    [Fact]
    public void A_known_var_cannot_be_removed()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");
        vm.RemoveSelectedVar();

        vm.Status.Should().StartWith("✗", "standard engine vars revert to default if missing — not user data");
        Reloaded(cfg).Rows.Select(r => r.Name).Should().Contain("AnimalMaxCount");
    }

    [Fact]
    public void ResetToDefault_restores_the_engine_default()
    {
        var vm = Load(out var cfg);
        var row = vm.Rows.Single(r => r.Name == "AnimalMaxCount");   // tuned to 100
        vm.ResetToDefault(row);

        Reloaded(cfg).Rows.Single(r => r.Name == "AnimalMaxCount").Value
            .Should().Be("200", "AnimalMaxCount's engine default is 200");
    }

    [Fact]
    public void A_custom_var_can_be_renamed_then_removed()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "MyCustomTweak");
        vm.RenameText = "MyTweak2";
        vm.CommitRename();
        Reloaded(cfg).Rows.Select(r => r.Name).Should().Contain("MyTweak2").And.NotContain("MyCustomTweak");

        var vm2 = Reloaded(cfg);
        vm2.RemoveVar(vm2.Rows.Single(r => r.Name == "MyTweak2"));   // confirm => true
        Reloaded(cfg).Rows.Select(r => r.Name).Should().NotContain("MyTweak2");
    }

    [Fact]
    public void MissingKnown_excludes_present_vars()
    {
        var vm = Load(out _);
        vm.MissingKnown.Should().NotContain("AnimalMaxCount", "it's already present");
        vm.MissingKnown.Should().Contain("CleanupAvoidance", "a standard var absent from the file");
    }

    [Fact]
    public void AddKnown_adds_a_missing_standard_var_seeded_with_its_default()
    {
        var vm = Load(out var cfg);
        vm.SelectedMissing = "CleanupAvoidance";
        vm.AddKnown();

        var added = Reloaded(cfg).Rows.Single(r => r.Name == "CleanupAvoidance");
        added.Value.Should().Be("100", "seeded with the engine default");
        added.Type.Should().Be(0);
    }
}
