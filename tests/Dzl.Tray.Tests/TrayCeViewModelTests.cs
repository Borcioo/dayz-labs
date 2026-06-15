using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>
/// Tier-1 ViewModel regression tests for CE Tray view-models (bug-hunt 2026-06-14). Each constructs the real
/// VM against a temp config + mission (the same scaffold pattern as the Core service tests), drives it the
/// way the editor controls do, and reloads a fresh VM to assert what landed on disk. No STA/Application needed.
///
/// Covers the VM-layer defects the bug-hunt confirmed: PlayerSpawns param rename orphaning the old element,
/// and the "Add" affordances silently overwriting an existing entry. (The RandomPresets filter-selection drop
/// is a WPF-binding side effect — not reproducible headless — and is fixed by parity with SpawnableTypesVm.)
/// </summary>
public class TrayCeViewModelTests
{
    /// <summary>Build a temp mission with the supplied CE files and return the config path.</summary>
    private static string Scaffold(string? playerSpawns = null, string? globals = null)
    {
        var files = new List<(string, string)>();
        if (playerSpawns is not null) files.Add(("cfgplayerspawnpoints.xml", playerSpawns));
        if (globals is not null) files.Add(("db/globals.xml", globals));
        return CeScaffold.Mission(files.ToArray());
    }

    private const string PlayerSpawnsXml = """
        <playerspawnpoints>
          <fresh>
            <spawn_params>
              <grid_width>200</grid_width>
            </spawn_params>
          </fresh>
        </playerspawnpoints>
        """;

    private const string GlobalsXml = """
        <variables>
          <var name="AnimalMaxCount" type="0" value="100"/>
        </variables>
        """;

    // ── BUG A (HIGH): renaming a param's Name cell must rename the element in place, not add a new param
    //    and leave the old one orphaned.
    [Fact]
    public void PlayerSpawns_param_rename_renames_in_place_without_orphaning_the_old_element()
    {
        var cfg = Scaffold(playerSpawns: PlayerSpawnsXml);

        var vm = new PlayerSpawnsVm(cfg, _ => true);
        vm.Reload();
        vm.SelectedCategory = "fresh";
        vm.OtherParams.Should().ContainSingle().Which.Name.Should().Be("grid_width");

        var param = vm.OtherParams.Single();
        param.Name = "grid_height";   // user edits the Name cell …
        param.Commit();               // … and commits (LostFocus / Enter)

        var reloaded = new PlayerSpawnsVm(cfg, _ => true);
        reloaded.Reload();
        reloaded.SelectedCategory = "fresh";

        reloaded.OtherParams.Select(p => p.Name).Should().ContainSingle()
            .Which.Should().Be("grid_height", "the param must be renamed in place, leaving no grid_width orphan");
        reloaded.OtherParams.Single().Value.Should().Be("200", "the value survives the rename");
    }

    // ── BUG C (MED): the spawn-params "Add" form must reject a name already present in the section instead of
    //    silently overwriting that param's value.
    [Fact]
    public void PlayerSpawns_AddParam_rejects_an_existing_name_and_preserves_the_value()
    {
        var cfg = Scaffold(playerSpawns: PlayerSpawnsXml);

        var vm = new PlayerSpawnsVm(cfg, _ => true);
        vm.Reload();
        vm.SelectedCategory = "fresh";

        vm.AddOtherParam("spawn_params", "grid_width", "999");
        vm.Status.Should().StartWith("✗", "adding an existing param name must be rejected");

        var reloaded = new PlayerSpawnsVm(cfg, _ => true);
        reloaded.Reload();
        reloaded.SelectedCategory = "fresh";
        reloaded.OtherParams.Single(p => p.Name == "grid_width").Value
            .Should().Be("200", "the existing param value must not be clobbered by an Add");
    }

    // ── globals.xml is a CLOSED engine vocabulary: a standard variable is not user data — removing it only
    //    reverts the engine to its default. The editor must refuse to delete a standard var (reset instead).
    [Fact]
    public void GlobalsVm_standard_var_is_not_removable_and_keeps_its_value()
    {
        var cfg = Scaffold(globals: GlobalsXml);

        var vm = new GlobalsVm(cfg, _ => true);
        vm.Reload();
        vm.SelectedRow = vm.Rows.Single(r => r.Name == "AnimalMaxCount");
        vm.RemoveSelectedVar();
        vm.Status.Should().StartWith("✗", "a standard engine variable must not be removable");

        var reloaded = new GlobalsVm(cfg, _ => true);
        reloaded.Reload();
        reloaded.Rows.Single(r => r.Name == "AnimalMaxCount").Value
            .Should().Be("100", "the standard var stays put");
    }
}
