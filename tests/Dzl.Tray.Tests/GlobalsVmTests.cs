using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="GlobalsVm"/> (Economy "Globals" tab) beyond the AddVar duplicate guard
/// (in TrayCeViewModelTests): load/filter, inline rename via CommitRowEdit, value edit, and remove.</summary>
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
    public void CommitRowEdit_renames_in_place()
    {
        var vm = Load(out var cfg);
        var row = vm.Rows.Single(r => r.Name == "AnimalMaxCount");

        row.Name = "AnimalCountMax";   // inline rename in the grid
        vm.CommitRowEdit(row);

        var reloaded = Reloaded(cfg);
        reloaded.Rows.Select(r => r.Name).Should().Contain("AnimalCountMax").And.NotContain("AnimalMaxCount");
    }

    [Fact]
    public void CommitRowEdit_persists_a_value_change()
    {
        var vm = Load(out var cfg);
        var row = vm.Rows.Single(r => r.Name == "ZombieMaxCount");

        row.Value = "500";
        vm.CommitRowEdit(row);

        Reloaded(cfg).Rows.Single(r => r.Name == "ZombieMaxCount").Value.Should().Be("500");
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
