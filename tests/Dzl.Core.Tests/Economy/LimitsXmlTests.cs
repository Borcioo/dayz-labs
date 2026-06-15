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

    [Fact]
    public void WithCombos_folds_combo_names_into_the_matching_set_case_insensitively()
    {
        var d = LimitsXml.Parse(Xml).WithCombos(new[]
        {
            new LimitsUserGroup("TownVillage", LimitsKind.Usage, new[] { "Military", "Police" }),
            new LimitsUserGroup("Tier123", LimitsKind.Value, new[] { "Tier1" }),
        });

        d.Usage.Contains("TownVillage").Should().BeTrue();
        d.Usage.Contains("townvillage").Should().BeTrue("the folded set stays case-insensitive");
        d.Value.Contains("Tier123").Should().BeTrue();
        d.Value.Contains("tier123").Should().BeTrue("the folded set stays case-insensitive");
        d.Usage.Should().Contain("Military", "base flags are preserved");
        d.Tag.Should().BeEquivalentTo(new[] { "floor" }, "combos never touch tags");
    }

    [Fact]
    public void WithCombos_with_no_combos_returns_the_same_instance()
    {
        var d = LimitsXml.Parse(Xml);
        d.WithCombos(System.Array.Empty<LimitsUserGroup>()).Should().BeSameAs(d);
    }
}
