using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="EconomyCoreVm"/> (Economy "Economy core" tab = db/economy.xml). Closed engine
/// vocabulary: standard groups are flagged IsKnown (toggles editable, not removable → reset-to-default), only a
/// custom group is removable, and "Add" offers only known-missing groups.</summary>
public class EconomyCoreVmTests
{
    // dynamic is a standard group (here tweaked to all-off); myCustom is non-standard.
    private const string Fixture = """
        <economy>
          <dynamic init="0" load="0" respawn="0" save="0"/>
          <myCustom init="1" load="0" respawn="0" save="0"/>
        </economy>
        """;

    private static EconomyCoreVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("db/economy.xml", Fixture));
        var vm = new EconomyCoreVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static EconomyCoreVm Reloaded(string cfg)
    {
        var vm = new EconomyCoreVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_groups_and_flags_known_vs_custom()
    {
        var vm = Load(out _);
        vm.Groups.Select(g => g.Name).Should().BeEquivalentTo("dynamic", "myCustom");
        vm.Groups.Single(g => g.Name == "dynamic").IsKnown.Should().BeTrue();
        vm.Groups.Single(g => g.Name == "myCustom").IsKnown.Should().BeFalse();
    }

    [Fact]
    public void Toggling_a_flag_persists()
    {
        var vm = Load(out var cfg);
        vm.Groups.Single(g => g.Name == "dynamic").Save = true;

        Reloaded(cfg).Groups.Single(g => g.Name == "dynamic").Save.Should().BeTrue();
    }

    [Fact]
    public void ResetToDefault_restores_the_vanilla_flags()
    {
        var vm = Load(out var cfg);
        vm.ResetToDefault(vm.Groups.Single(g => g.Name == "dynamic"));

        var dyn = Reloaded(cfg).Groups.Single(g => g.Name == "dynamic");
        (dyn.Init, dyn.Load, dyn.Respawn, dyn.Save).Should().Be((true, true, true, true), "vanilla dynamic is all-on");
    }

    [Fact]
    public void A_standard_group_is_not_removable()
    {
        var vm = Load(out var cfg);
        vm.RemoveGroup(vm.Groups.Single(g => g.Name == "dynamic"));

        vm.Status.Should().StartWith("✗", "standard groups revert to default if missing — not removable");
        Reloaded(cfg).Groups.Select(g => g.Name).Should().Contain("dynamic");
    }

    [Fact]
    public void A_custom_group_can_be_removed()
    {
        var vm = Load(out var cfg);
        vm.RemoveGroup(vm.Groups.Single(g => g.Name == "myCustom"));   // confirm => true

        Reloaded(cfg).Groups.Select(g => g.Name).Should().NotContain("myCustom");
    }

    [Fact]
    public void MissingKnown_lists_only_absent_standard_groups()
    {
        var vm = Load(out _);
        vm.MissingKnown.Should().NotContain("dynamic", "it's present");
        vm.MissingKnown.Should().Contain("zombies", "a standard group absent from the file");
    }

    [Fact]
    public void AddKnown_adds_a_missing_group_seeded_with_its_defaults()
    {
        var vm = Load(out var cfg);
        vm.SelectedMissing = "zombies";
        vm.AddKnown();

        var z = Reloaded(cfg).Groups.Single(g => g.Name == "zombies");
        (z.Init, z.Load, z.Respawn, z.Save).Should().Be((true, false, true, false), "vanilla zombies defaults");
    }
}
