using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="CeCoreVm"/> (Economy "CE Config" tab = cfgeconomycore.xml): default knobs
/// (bool toggle + numeric, grouped, validated), the custom-file routing manifest, and add-missing-default.</summary>
public class CeCoreVmTests
{
    private const string Fixture = """
        <economycore>
          <classes><rootclass name="SurvivorBase" act="character"/></classes>
          <defaults>
            <default name="dyn_radius" value="30"/>
            <default name="log_ce_loop" value="false"/>
          </defaults>
        </economycore>
        """;

    private static CeCoreVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgeconomycore.xml", Fixture));
        var vm = new CeCoreVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static CeCoreVm Reloaded(string cfg)
    {
        var vm = new CeCoreVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_surfaces_defaults_groups_and_root_classes()
    {
        var vm = Load(out _);
        vm.Defaults.Select(d => d.Name).Should().Contain(new[] { "dyn_radius", "log_ce_loop" });
        vm.DefaultGroups.Should().NotBeEmpty();
        vm.RootClasses.Should().ContainSingle(r => r.Name == "SurvivorBase");
        vm.HasFiles.Should().BeFalse("vanilla registers no custom files");
    }

    [Fact]
    public void Toggling_a_bool_default_persists()
    {
        var vm = Load(out var cfg);
        vm.Defaults.Single(d => d.Name == "log_ce_loop").Flag = true;
        Reloaded(cfg).Defaults.Single(d => d.Name == "log_ce_loop").Flag.Should().BeTrue();
    }

    [Fact]
    public void Editing_a_numeric_default_persists()
    {
        var vm = Load(out var cfg);
        var d = vm.Defaults.Single(x => x.Name == "dyn_radius");
        d.Text = "45";
        d.Commit();
        Reloaded(cfg).Defaults.Single(x => x.Name == "dyn_radius").Text.Should().Be("45");
    }

    [Fact]
    public void A_numeric_default_rejects_a_non_number()
    {
        var vm = Load(out var cfg);
        var d = vm.Defaults.Single(x => x.Name == "dyn_radius");
        d.Text = "abc";
        d.Commit();
        vm.Status.Should().StartWith("✗");
        Reloaded(cfg).Defaults.Single(x => x.Name == "dyn_radius").Text.Should().Be("30");
    }

    [Fact]
    public void AddFile_registers_a_custom_file_then_RemoveFile_unregisters()
    {
        var vm = Load(out var cfg);
        vm.NewFileFolder = "ce/MyMod";
        vm.NewFileName = "mymod_types.xml";
        vm.NewFileType = "types";
        vm.AddFile();
        Reloaded(cfg).Files.Should().ContainSingle(f => f.Name == "mymod_types.xml" && f.Type == "types");

        var vm2 = Reloaded(cfg);
        vm2.RemoveFile(vm2.Files.Single(f => f.Name == "mymod_types.xml"));   // confirm => true
        Reloaded(cfg).Files.Should().BeEmpty();
    }

    [Fact]
    public void AddMissingDefault_adds_with_its_engine_default()
    {
        var vm = Load(out var cfg);
        vm.MissingDefaults.Should().Contain("backup_count");
        vm.SelectedMissingDefault = "backup_count";
        vm.AddMissingDefault();
        Reloaded(cfg).Defaults.Single(d => d.Name == "backup_count").Text.Should().Be("12");
    }
}
