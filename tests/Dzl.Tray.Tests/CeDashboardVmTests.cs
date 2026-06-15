using System.IO;
using Dzl.Core.App;
using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="CeDashboardVm"/> (Economy "Dashboard" tab): the auto-validation staleness
/// guard — open/return to the dashboard runs the full cross-file validation only when a CE file changed,
/// not on every tab toggle. Drives the real VM over a temp mission.</summary>
public class CeDashboardVmTests
{
    private const string Presets = """
        <randompresets>
          <cargo chance="0.1" name="p"><item name="A" chance="1"/></cargo>
        </randompresets>
        """;

    [Fact]
    public void FileSignature_is_stable_with_no_edits_and_changes_when_a_file_appears()
    {
        var cfg = CeScaffold.Mission(("cfgrandompresets.xml", Presets));
        var first = new CeWorldLoader(cfg).Load();
        var sig = CeDashboardVm.FileSignature(first);

        CeDashboardVm.FileSignature(new CeWorldLoader(cfg).Load())
            .Should().Be(sig, "no on-disk edit → identical token");

        // A CE file that didn't exist now does → its Files entry flips, so the token must change.
        File.WriteAllText(Path.Combine(first.MissionDir, "cfgspawnabletypes.xml"), "<spawnabletypes/>");
        CeDashboardVm.FileSignature(new CeWorldLoader(cfg).Load())
            .Should().NotBe(sig, "a new CE file changes the token");
    }

    [Fact]
    public async Task RefreshAndValidate_runs_once_on_open_skips_when_unchanged_reruns_after_an_edit()
    {
        var cfg = CeScaffold.Mission(("cfgrandompresets.xml", Presets));
        var vm = new CeDashboardVm(cfg, _ => true);

        await vm.RefreshAndValidateAsync();
        vm.ValidationRunCount.Should().Be(1, "first show validates");
        vm.IsValidating.Should().BeFalse();

        await vm.RefreshAndValidateAsync();
        vm.ValidationRunCount.Should().Be(1, "nothing changed → no re-validation on tab return");

        // Edit a CE file → the staleness token differs → the next show re-validates.
        File.WriteAllText(Path.Combine(vm.MissionDir, "cfgspawnabletypes.xml"), "<spawnabletypes/>");
        await vm.RefreshAndValidateAsync();
        vm.ValidationRunCount.Should().Be(2, "a changed CE file re-validates");
    }
}
