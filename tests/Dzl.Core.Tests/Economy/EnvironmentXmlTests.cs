using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of cfgenvironment.xml (territories, item knobs, file list).</summary>
public class EnvironmentXmlTests
{
    private const string Xml = """
        <env>
          <territories>
            <file path="env/hen_territories.xml"/>
            <file path="env/wolf_territories.xml"/>
            <territory type="Ambient" name="AmbientHen" behavior="DZAmbientLifeGroupBeh">
              <file usable="hen_territories"/>
              <agent type="Male" chance="1"><spawn configName="Animal_GallusGallusDomesticus" chance="1"/></agent>
              <item name="globalCountMax" val="50"/>
              <item name="zoneCountMax" val="1"/>
            </territory>
          </territories>
        </env>
        """;

    [Fact]
    public void Parse_reads_files_territories_items_and_spawns()
    {
        var c = EnvironmentXml.Parse(Xml);
        c.Files.Should().BeEquivalentTo("env/hen_territories.xml", "env/wolf_territories.xml");
        var t = c.Territories.Single(x => x.Name == "AmbientHen");
        t.Type.Should().Be("Ambient");
        t.UsableFile.Should().Be("hen_territories");
        t.Items.Select(i => i.Name).Should().Contain(new[] { "globalCountMax", "zoneCountMax" });
        t.Spawns.Should().ContainSingle().Which.ConfigName.Should().Be("Animal_GallusGallusDomesticus");
    }

    [Fact]
    public void SetItem_upserts_a_territory_level_knob()
    {
        var doc = EnvironmentXml.ParseDoc(Xml);
        EnvironmentXml.SetItem(doc, "AmbientHen", "globalCountMax", "80").Should().BeTrue();
        EnvironmentXml.SetItem(doc, "AmbientHen", "zoneCountMin", "1").Should().BeTrue();
        var t = EnvironmentXml.Parse(EnvironmentXml.ToXml(doc)).Territories.Single(x => x.Name == "AmbientHen");
        t.Items.Single(i => i.Name == "globalCountMax").Val.Should().Be("80");
        t.Items.Should().Contain(i => i.Name == "zoneCountMin" && i.Val == "1");
    }

    [Fact]
    public void SetItem_fails_for_a_missing_territory()
    {
        var doc = EnvironmentXml.ParseDoc(Xml);
        EnvironmentXml.SetItem(doc, "Nope", "x", "1").Should().BeFalse();
    }

    [Fact]
    public void Parse_is_empty_on_malformed()
    {
        EnvironmentXml.Parse("garbage").Territories.Should().BeEmpty();
    }
}
