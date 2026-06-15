using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="GlobalsVm"/> (Economy "Globals" tab) beyond the AddVar duplicate guard
/// (in TrayCeViewModelTests): load/filter, detail-pane rename (CommitRename) + value/type edit (SaveDetail),
/// and remove.</summary>
public class GlobalsVmTests
{
    private const string Fixture = """
        <variables>
          <var name="AnimalMaxCount" type="0" value="100"/>
          <var name="ZombieMaxCount" type="0" value="200"/>
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
        vm.Rows.Select(r => r.Name).Should().BeEquivalentTo("AnimalMaxCount", "ZombieMaxCount");

        vm.Filter = "zombie";
        vm.Rows.Select(r => r.Name).Should().ContainSingle().Which.Should().Be("ZombieMaxCount");
    }

    [Fact]
    public void CommitRename_renames_the_selected_var_via_the_detail_box()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");

        vm.RenameText = "AnimalCountMax";   // detail-pane rename box
        vm.CommitRename();

        var reloaded = Reloaded(cfg);
        reloaded.Rows.Select(r => r.Name).Should().Contain("AnimalCountMax").And.NotContain("AnimalMaxCount");
    }

    [Fact]
    public void SaveDetail_persists_a_value_and_type_change()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "ZombieMaxCount");

        vm.DetailValue = "500";
        vm.DetailType = 1;     // float
        vm.SaveDetail();

        var saved = Reloaded(cfg).Rows.Single(r => r.Name == "ZombieMaxCount");
        saved.Value.Should().Be("500");
        saved.Type.Should().Be(1);
    }

    [Fact]
    public void SaveDetail_rejects_a_non_numeric_value()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");

        vm.DetailValue = "potato";
        vm.SaveDetail();

        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Rows.Single(r => r.Name == "AnimalMaxCount").Value
            .Should().Be("100", "the non-numeric value must not be persisted");
    }

    [Fact]
    public void SaveDetail_rejects_a_decimal_for_an_int_typed_var()
    {
        var vm = Load(out _);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");   // type 0 = int

        vm.DetailValue = "1.5";
        vm.SaveDetail();

        vm.Status.Should().StartWith("✗", "a decimal is invalid for an int-typed global");
    }

    [Fact]
    public void AddVar_rejects_a_non_numeric_value()
    {
        var vm = Load(out var cfg);
        vm.NewVarName = "NewVar";
        vm.NewVarType = 1;          // float
        vm.NewVarValue = "abc";
        vm.AddVar();

        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Rows.Select(r => r.Name).Should().NotContain("NewVar");
    }

    [Fact]
    public void RemoveSelectedVar_deletes_it()
    {
        var vm = Load(out var cfg);
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");

        vm.RemoveSelectedVar();   // confirm => true

        Reloaded(cfg).Rows.Select(r => r.Name).Should().NotContain("AnimalMaxCount");
    }
}
