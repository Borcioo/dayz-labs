using Dzl.Core.Config;
using FluentAssertions;

public class UiStateTests
{
    private static string TmpConfig() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");

    [Fact]
    public void Packs_are_collapsed_by_default_and_SetPackExpanded_is_case_insensitive()
    {
        var s = new UiState();
        s.IsPackExpanded("DemoPack").Should().BeFalse();   // default = collapsed
        s.SetPackExpanded("DemoPack", true);
        s.IsPackExpanded("demopack").Should().BeTrue();    // case-insensitive
        s.SetPackExpanded("DEMOPACK", false);
        s.IsPackExpanded("DemoPack").Should().BeFalse();
    }

    [Fact]
    public void Save_then_Load_round_trips_the_expanded_set()
    {
        var cfg = TmpConfig();
        var s = new UiState();
        s.SetPackExpanded("Balticrus", true);
        s.SetPackExpanded("DemoPack", true);
        s.Save(cfg);

        File.Exists(UiState.PathFor(cfg)).Should().BeTrue();
        var loaded = UiState.Load(cfg);
        loaded.ExpandedPacks.Should().BeEquivalentTo(new[] { "Balticrus", "DemoPack" });
        loaded.IsPackExpanded("balticrus").Should().BeTrue();   // comparer preserved across load
    }

    [Fact]
    public void Load_is_empty_when_no_file_exists()
        => UiState.Load(TmpConfig()).ExpandedPacks.Should().BeEmpty();

    [Fact]
    public void Load_is_empty_and_does_not_throw_on_a_corrupt_file()
    {
        var cfg = TmpConfig();
        File.WriteAllText(UiState.PathFor(cfg), "{ not valid json ]");
        UiState.Load(cfg).ExpandedPacks.Should().BeEmpty();
    }
}
