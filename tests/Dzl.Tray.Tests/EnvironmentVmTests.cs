using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="EnvironmentVm"/> (World "Environment" tab = cfgenvironment.xml): territory
/// list, editable item knobs + read-only spawns, and the referenced env-file shortcuts.</summary>
public class EnvironmentVmTests
{
    private const string Fixture = """
        <env>
          <territories>
            <file path="env/hen_territories.xml"/>
            <territory type="Ambient" name="AmbientHen" behavior="DZAmbientLifeGroupBeh">
              <file usable="hen_territories"/>
              <agent type="Male" chance="1"><spawn configName="Animal_GallusGallusDomesticus" chance="1"/></agent>
              <item name="globalCountMax" val="50"/>
            </territory>
          </territories>
        </env>
        """;

    private static EnvironmentVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgenvironment.xml", Fixture));
        var vm = new EnvironmentVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static EnvironmentVm Reloaded(string cfg)
    {
        var vm = new EnvironmentVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_territories_items_spawns_and_files()
    {
        var vm = Load(out _);
        vm.Territories.Should().ContainSingle(t => t.Name == "AmbientHen");
        vm.SelectedTerritory = vm.Territories.Single();
        vm.Items.Should().ContainSingle(i => i.Name == "globalCountMax");
        vm.Spawns.Should().ContainSingle();
        vm.Files.Should().ContainSingle(f => f.Name == "env/hen_territories.xml");
    }

    [Fact]
    public void Editing_an_item_persists()
    {
        var vm = Load(out var cfg);
        vm.SelectedTerritory = vm.Territories.Single();
        var item = vm.Items.Single(i => i.Name == "globalCountMax");
        item.ValText = "80";
        item.Commit();

        var reloaded = Reloaded(cfg);
        reloaded.SelectedTerritory = reloaded.Territories.Single();
        reloaded.Items.Single(i => i.Name == "globalCountMax").ValText.Should().Be("80");
    }

    [Fact]
    public void A_non_numeric_item_is_rejected()
    {
        var vm = Load(out var cfg);
        vm.SelectedTerritory = vm.Territories.Single();
        var item = vm.Items.Single(i => i.Name == "globalCountMax");
        item.ValText = "lots";
        item.Commit();
        vm.Status.Should().StartWith("✗");

        var reloaded = Reloaded(cfg);
        reloaded.SelectedTerritory = reloaded.Territories.Single();
        reloaded.Items.Single(i => i.Name == "globalCountMax").ValText.Should().Be("50");
    }
}
