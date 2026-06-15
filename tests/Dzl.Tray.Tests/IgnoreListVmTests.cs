using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="IgnoreListVm"/> (Economy "Ignore list" tab = cfgignorelist.xml): load/filter,
/// add (validated identifier, dedup) and remove.</summary>
public class IgnoreListVmTests
{
    private const string Fixture = """
        <ignore>
          <type name="Bandage"></type>
          <type name="Defibrillator"></type>
        </ignore>
        """;

    private static IgnoreListVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgignorelist.xml", Fixture));
        var vm = new IgnoreListVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static IgnoreListVm Reloaded(string cfg)
    {
        var vm = new IgnoreListVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_lists_classnames_and_filter_narrows()
    {
        var vm = Load(out _);
        vm.Items.Should().BeEquivalentTo("Bandage", "Defibrillator");
        vm.Filter = "defib";
        vm.Items.Should().ContainSingle().Which.Should().Be("Defibrillator");
    }

    [Fact]
    public void AddName_adds_a_valid_classname()
    {
        var vm = Load(out var cfg);
        vm.NewName = "EngineOil";
        vm.AddName();
        Reloaded(cfg).Items.Should().Contain("EngineOil");
    }

    [Fact]
    public void AddName_rejects_a_non_identifier()
    {
        var vm = Load(out var cfg);
        vm.NewName = "Bad Name <x>";
        vm.AddName();
        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Items.Should().NotContain("Bad Name <x>");
    }

    [Fact]
    public void AddName_rejects_a_duplicate()
    {
        var vm = Load(out var cfg);
        vm.NewName = "bandage";   // case-insensitive dup
        vm.AddName();
        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Items.Count(n => string.Equals(n, "Bandage", System.StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public void RemoveName_removes_it()
    {
        var vm = Load(out var cfg);
        vm.RemoveName("Bandage");
        Reloaded(cfg).Items.Should().NotContain("Bandage");
    }
}
