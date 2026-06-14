using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="AutoSuggest.Filter"/> — the one shared filtering rule behind the reusable
/// <see cref="AutoSuggestBox"/> (used by the Random Presets and Spawnable Types classname fields).</summary>
public class AutoSuggestTests
{
    private static readonly string[] Pool = { "Nail", "Plank", "TacticalBacon", "AK_PlasticBttstck", "TunaCan" };

    [Fact]
    public void Blank_query_returns_nothing()
    {
        AutoSuggest.Filter(Pool, "", 50).Should().BeEmpty();
        AutoSuggest.Filter(Pool, "   ", 50).Should().BeEmpty("a whitespace-only query is treated as empty");
    }

    [Fact]
    public void Matches_substring_case_insensitively()
    {
        AutoSuggest.Filter(Pool, "nai", 50).Should().ContainSingle().Which.Should().Be("Nail");
        AutoSuggest.Filter(Pool, "an", 50).Should().BeEquivalentTo("Plank", "TunaCan");
    }

    [Fact]
    public void Caps_results_at_max()
    {
        var big = Enumerable.Range(0, 200).Select(i => $"Item{i}").ToArray();
        AutoSuggest.Filter(big, "item", 50).Should().HaveCount(50);
    }

    [Fact]
    public void Skips_null_or_empty_pool_entries()
    {
        AutoSuggest.Filter(new[] { "Nail", "", null!, "Naodd" }, "na", 50)
            .Should().BeEquivalentTo("Nail", "Naodd");
    }
}
