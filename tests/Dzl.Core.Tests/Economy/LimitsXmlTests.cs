using Dzl.Core.Economy;
using FluentAssertions;

public class LimitsXmlTests
{
    private const string Xml = """
    <lists>
      <categories><category name="weapons"/><category name="food"/></categories>
      <tags><tag name="floor"/></tags>
      <usageflags><usage name="Military"/><usage name="Police"/></usageflags>
      <valueflags><value name="Tier1"/></valueflags>
    </lists>
    """;

    [Fact]
    public void Parse_collects_each_definition_set()
    {
        var d = LimitsXml.Parse(Xml);
        d.Category.Should().BeEquivalentTo("weapons", "food");
        d.Tag.Should().BeEquivalentTo("floor");
        d.Usage.Should().BeEquivalentTo("Military", "Police");
        d.Value.Should().BeEquivalentTo("Tier1");
    }

    [Fact]
    public void Parse_returns_empty_sets_on_malformed_or_empty()
    {
        LimitsXml.Parse("<nope/>").Usage.Should().BeEmpty();
        LimitsXml.Parse("garbage").Usage.Should().BeEmpty();   // must NOT throw
    }

    [Fact]
    public void Sets_are_case_insensitive()
    {
        var d = LimitsXml.Parse(Xml);
        d.Usage.Contains("military").Should().BeTrue();
    }
}
