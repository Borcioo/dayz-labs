using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of cfgeconomycore.xml (root classes, defaults, custom-file routing).</summary>
public class CeCoreXmlTests
{
    private const string Xml = """
        <economycore>
          <classes>
            <rootclass name="DefaultWeapon"/>
            <rootclass name="SurvivorBase" act="character" reportMemoryLOD="no"/>
          </classes>
          <defaults>
            <default name="dyn_radius" value="30"/>
            <default name="log_ce_loop" value="false"/>
          </defaults>
        </economycore>
        """;

    [Fact]
    public void Parse_reads_classes_defaults_and_no_routing()
    {
        var c = CeCoreXml.Parse(Xml);
        c.RootClasses.Single(r => r.Name == "SurvivorBase").Act.Should().Be("character");
        c.RootClasses.Single(r => r.Name == "SurvivorBase").ReportMemoryLod.Should().BeFalse();
        c.RootClasses.Single(r => r.Name == "DefaultWeapon").ReportMemoryLod.Should().BeTrue("absent reportMemoryLOD defaults to yes");
        c.Defaults.Select(d => d.Name).Should().Contain(new[] { "dyn_radius", "log_ce_loop" });
        c.Files.Should().BeEmpty();
    }

    [Fact]
    public void Parse_is_empty_on_malformed()
    {
        CeCoreXml.Parse("garbage").Defaults.Should().BeEmpty();
    }

    [Fact]
    public void SetDefault_upserts()
    {
        var doc = CeCoreXml.ParseDoc(Xml);
        CeCoreXml.SetDefault(doc, "dyn_radius", "50").Should().BeTrue();
        CeCoreXml.SetDefault(doc, "world_segments", "1").Should().BeTrue();
        var c = CeCoreXml.Parse(CeCoreXml.ToXml(doc));
        c.Defaults.Single(d => d.Name == "dyn_radius").Value.Should().Be("50");
        c.Defaults.Should().Contain(d => d.Name == "world_segments" && d.Value == "1");
    }

    [Fact]
    public void AddFile_registers_rejecting_dup_and_bad_type()
    {
        var doc = CeCoreXml.ParseDoc(Xml);
        CeCoreXml.AddFile(doc, "ce/MyMod", "mymod_types.xml", "types").Should().BeTrue();
        CeCoreXml.AddFile(doc, "ce/MyMod", "mymod_types.xml", "types").Should().BeFalse("duplicate folder+name");
        CeCoreXml.AddFile(doc, "ce/MyMod", "x.xml", "bogus").Should().BeFalse("invalid type");
        CeCoreXml.Parse(CeCoreXml.ToXml(doc)).Files.Should().ContainSingle()
            .Which.Should().Be(new CeRoutedFile("ce/MyMod", "mymod_types.xml", "types"));
    }

    [Fact]
    public void RemoveFile_removes_and_drops_the_empty_ce_block()
    {
        var doc = CeCoreXml.ParseDoc(Xml);
        CeCoreXml.AddFile(doc, "ce/MyMod", "a.xml", "events");
        CeCoreXml.RemoveFile(doc, "ce/MyMod", "a.xml").Should().BeTrue();
        CeCoreXml.Parse(CeCoreXml.ToXml(doc)).Files.Should().BeEmpty();
    }
}
