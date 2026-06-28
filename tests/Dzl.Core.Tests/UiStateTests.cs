using Dzl.Core.Config;
using FluentAssertions;

public class UiStateTests
{
    private static string TmpConfig() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");

    [Fact]
    public void SetPackCollapsed_toggles_and_is_case_insensitive()
    {
        var s = new UiState();
        s.IsPackCollapsed("DemoPack").Should().BeFalse();
        s.SetPackCollapsed("DemoPack", true);
        s.IsPackCollapsed("demopack").Should().BeTrue();   // case-insensitive
        s.SetPackCollapsed("DEMOPACK", false);
        s.IsPackCollapsed("DemoPack").Should().BeFalse();
    }

    [Fact]
    public void Save_then_Load_round_trips_the_collapsed_set()
    {
        var cfg = TmpConfig();
        var s = new UiState();
        s.SetPackCollapsed("Balticrus", true);
        s.SetPackCollapsed("DemoPack", true);
        s.Save(cfg);

        File.Exists(UiState.PathFor(cfg)).Should().BeTrue();
        var loaded = UiState.Load(cfg);
        loaded.CollapsedPacks.Should().BeEquivalentTo(new[] { "Balticrus", "DemoPack" });
        loaded.IsPackCollapsed("balticrus").Should().BeTrue();   // comparer preserved across load
    }

    [Fact]
    public void Load_is_empty_when_no_file_exists()
        => UiState.Load(TmpConfig()).CollapsedPacks.Should().BeEmpty();

    [Fact]
    public void Load_is_empty_and_does_not_throw_on_a_corrupt_file()
    {
        var cfg = TmpConfig();
        File.WriteAllText(UiState.PathFor(cfg), "{ not valid json ]");
        UiState.Load(cfg).CollapsedPacks.Should().BeEmpty();
    }
}
