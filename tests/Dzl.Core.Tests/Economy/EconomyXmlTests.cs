using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of db/economy.xml (per-group init/load/respawn/save toggles) and the
/// known-group catalog.</summary>
public class EconomyXmlTests
{
    private const string Xml = """
        <economy>
          <dynamic init="1" load="1" respawn="1" save="1"/>
          <animals init="1" load="0" respawn="1" save="0"/>
        </economy>
        """;

    [Fact]
    public void Parse_reads_groups_and_flags()
    {
        var g = EconomyXml.Parse(Xml);
        g.Select(x => x.Name).Should().BeEquivalentTo("dynamic", "animals");
        var an = g.Single(x => x.Name == "animals");
        an.Init.Should().BeTrue();
        an.Load.Should().BeFalse();
        an.Respawn.Should().BeTrue();
        an.Save.Should().BeFalse();
    }

    [Fact]
    public void Parse_is_empty_on_malformed_xml()
    {
        EconomyXml.Parse("garbage").Should().BeEmpty();
    }

    [Fact]
    public void SetFlag_toggles_an_existing_group()
    {
        var doc = EconomyXml.ParseDoc(Xml);
        EconomyXml.SetFlag(doc, "animals", "load", true).Should().BeTrue();
        EconomyXml.Parse(EconomyXml.ToXml(doc)).Single(x => x.Name == "animals").Load.Should().BeTrue();
    }

    [Fact]
    public void SetFlag_fails_for_a_missing_group_or_bad_flag()
    {
        var doc = EconomyXml.ParseDoc(Xml);
        EconomyXml.SetFlag(doc, "zombies", "init", true).Should().BeFalse("the group is absent");
        EconomyXml.SetFlag(doc, "dynamic", "bogus", true).Should().BeFalse("the flag is invalid");
    }

    [Fact]
    public void SetGroup_upserts_all_four_flags()
    {
        var doc = EconomyXml.ParseDoc(Xml);
        EconomyXml.SetGroup(doc, "zombies", init: true, load: false, respawn: true, save: false).Should().BeTrue();
        var z = EconomyXml.Parse(EconomyXml.ToXml(doc)).Single(x => x.Name == "zombies");
        (z.Init, z.Load, z.Respawn, z.Save).Should().Be((true, false, true, false));
    }

    [Fact]
    public void RemoveGroup_removes_it()
    {
        var doc = EconomyXml.ParseDoc(Xml);
        EconomyXml.RemoveGroup(doc, "animals").Should().BeTrue();
        EconomyXml.Parse(EconomyXml.ToXml(doc)).Select(x => x.Name).Should().NotContain("animals");
    }

    [Fact]
    public void Catalog_covers_the_vanilla_groups_case_insensitively()
    {
        EconomyCatalog.All.Should().HaveCount(8);
        EconomyCatalog.Find("DYNAMIC")!.Init.Should().BeTrue();
        EconomyCatalog.IsKnown("player").Should().BeTrue();
        EconomyCatalog.IsKnown("myCustom").Should().BeFalse();
    }
}
