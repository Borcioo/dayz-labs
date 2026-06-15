using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of cfgignorelist.xml (flat ignored-classname list).</summary>
public class IgnoreListXmlTests
{
    private const string Xml = """
        <ignore>
          <type name="Bandage"></type>
          <type name="Defibrillator"></type>
        </ignore>
        """;

    [Fact]
    public void Parse_reads_classnames()
    {
        IgnoreListXml.Parse(Xml).Should().BeEquivalentTo("Bandage", "Defibrillator");
    }

    [Fact]
    public void Parse_is_empty_on_malformed()
    {
        IgnoreListXml.Parse("garbage").Should().BeEmpty();
    }

    [Fact]
    public void Add_appends_rejecting_a_case_insensitive_duplicate()
    {
        var doc = IgnoreListXml.ParseDoc(Xml);
        IgnoreListXml.Add(doc, "Apple").Should().BeTrue();
        IgnoreListXml.Add(doc, "bandage").Should().BeFalse("case-insensitive duplicate");
        IgnoreListXml.Parse(IgnoreListXml.ToXml(doc)).Should().Contain("Apple");
    }

    [Fact]
    public void Remove_removes_it()
    {
        var doc = IgnoreListXml.ParseDoc(Xml);
        IgnoreListXml.Remove(doc, "Bandage").Should().BeTrue();
        IgnoreListXml.Parse(IgnoreListXml.ToXml(doc)).Should().NotContain("Bandage").And.Contain("Defibrillator");
    }
}
