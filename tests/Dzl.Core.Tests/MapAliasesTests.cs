using Xunit; using Dzl.Core.Servers; using FluentAssertions;
public class MapAliasesTests
{
    [Theory]
    [InlineData("chernarus", "dayzOffline.chernarusplus")]
    [InlineData("livonia", "dayzOffline.enoch")]
    [InlineData("sakhal", "dayzOffline.sakhal")]
    [InlineData("CHERNARUS", "dayzOffline.chernarusplus")]
    [InlineData("dayzOffline.namalsk", "dayzOffline.namalsk")]
    public void Resolve_alias_or_passthrough(string input, string expected)
        => MapAliases.MissionTemplate(input).Should().Be(expected);
}
