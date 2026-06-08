using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

/// <summary>Tests for LimitsUserXml — parse + in-place edit of cfglimitsdefinitionuser.xml.</summary>
public class LimitsUserXmlTests
{
    // Realistic fixture with usageflags (TownVillage) and valueflags (Tier123) groups.
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!-- cfglimitsdefinitionuser.xml — named flag combinations -->
        <user_lists>
          <usageflags>
            <!-- village combo -->
            <user name="TownVillage">
              <usage name="Town"/>
              <usage name="Village"/>
            </user>
            <user name="UrbanArea">
              <usage name="Town"/>
              <usage name="City"/>
            </user>
          </usageflags>
          <valueflags>
            <user name="Tier123">
              <value name="Tier1"/>
              <value name="Tier2"/>
              <value name="Tier3"/>
            </user>
          </valueflags>
        </user_lists>
        """;

    // ------------------------------------------------------------------
    // Parse
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_reads_usage_groups()
    {
        var groups = LimitsUserXml.Parse(Fixture);
        var tv = groups.FirstOrDefault(g => g.Name == "TownVillage");
        tv.Should().NotBeNull();
        tv!.Kind.Should().Be(LimitsKind.Usage);
        tv.Members.Should().BeEquivalentTo("Town", "Village");
    }

    [Fact]
    public void Parse_reads_value_groups()
    {
        var groups = LimitsUserXml.Parse(Fixture);
        var t123 = groups.FirstOrDefault(g => g.Name == "Tier123");
        t123.Should().NotBeNull();
        t123!.Kind.Should().Be(LimitsKind.Value);
        t123.Members.Should().BeEquivalentTo("Tier1", "Tier2", "Tier3");
    }

    [Fact]
    public void Parse_reads_all_groups()
    {
        var groups = LimitsUserXml.Parse(Fixture);
        groups.Count.Should().Be(3);  // TownVillage, UrbanArea (usage) + Tier123 (value)
    }

    [Fact]
    public void Parse_returns_empty_on_malformed_xml()
    {
        LimitsUserXml.Parse("garbage not xml").Should().BeEmpty();
    }

    [Fact]
    public void Parse_returns_empty_on_empty_string()
    {
        // Empty string is malformed XML — must not throw.
        LimitsUserXml.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Parse_returns_empty_on_empty_user_lists()
    {
        LimitsUserXml.Parse("<user_lists/>").Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // AddGroup
    // ------------------------------------------------------------------

    [Fact]
    public void AddGroup_adds_new_usage_group()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.AddGroup(doc, LimitsKind.Usage, "Rural", new[] { "Village", "Coast" });
        var xml = LimitsUserXml.ToXml(doc);
        var groups = LimitsUserXml.Parse(xml);
        var rural = groups.First(g => g.Name == "Rural");
        rural.Kind.Should().Be(LimitsKind.Usage);
        rural.Members.Should().BeEquivalentTo("Village", "Coast");
    }

    [Fact]
    public void AddGroup_replaces_existing_group_with_same_name()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.AddGroup(doc, LimitsKind.Usage, "TownVillage", new[] { "Town" });
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        var tv = groups.Where(g => g.Name == "TownVillage").ToList();
        tv.Count.Should().Be(1);  // no duplicates
        tv[0].Members.Should().BeEquivalentTo("Town");
    }

    [Fact]
    public void AddGroup_preserves_other_groups()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.AddGroup(doc, LimitsKind.Value, "Tier45", new[] { "Tier4", "Tier5" });
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        // Original groups still present.
        groups.Any(g => g.Name == "TownVillage").Should().BeTrue();
        groups.Any(g => g.Name == "Tier123").Should().BeTrue();
        groups.Any(g => g.Name == "Tier45").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // RemoveGroup
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveGroup_removes_existing_group()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.RemoveGroup(doc, LimitsKind.Usage, "TownVillage").Should().BeTrue();
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        groups.Any(g => g.Name == "TownVillage").Should().BeFalse();
    }

    [Fact]
    public void RemoveGroup_preserves_other_groups()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.RemoveGroup(doc, LimitsKind.Usage, "TownVillage");
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        groups.Any(g => g.Name == "UrbanArea").Should().BeTrue();
        groups.Any(g => g.Name == "Tier123").Should().BeTrue();
    }

    [Fact]
    public void RemoveGroup_returns_false_for_unknown_group()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.RemoveGroup(doc, LimitsKind.Usage, "Nonexistent").Should().BeFalse();
    }

    [Fact]
    public void RemoveGroup_case_insensitive()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.RemoveGroup(doc, LimitsKind.Value, "tier123").Should().BeTrue();
        LimitsUserXml.Parse(LimitsUserXml.ToXml(doc)).Any(g => g.Name == "Tier123").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // SetGroupMembers
    // ------------------------------------------------------------------

    [Fact]
    public void SetGroupMembers_updates_existing_group_in_place()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        var result = LimitsUserXml.SetGroupMembers(doc, LimitsKind.Usage, "TownVillage", new[] { "Town", "Village", "Hamlet" });
        result.Should().BeTrue();  // existed → true
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        var tv = groups.First(g => g.Name == "TownVillage");
        tv.Members.Should().BeEquivalentTo("Town", "Village", "Hamlet");
    }

    [Fact]
    public void SetGroupMembers_creates_group_when_absent()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        var result = LimitsUserXml.SetGroupMembers(doc, LimitsKind.Usage, "NewGroup", new[] { "Forest" });
        result.Should().BeFalse();  // did not exist → false
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        groups.First(g => g.Name == "NewGroup").Members.Should().BeEquivalentTo("Forest");
    }

    [Fact]
    public void SetGroupMembers_preserves_other_groups()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.SetGroupMembers(doc, LimitsKind.Value, "Tier123", new[] { "Tier1" });
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        groups.Any(g => g.Name == "TownVillage").Should().BeTrue();
        groups.Any(g => g.Name == "UrbanArea").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // ToXml round-trip preserves comments and order
    // ------------------------------------------------------------------

    [Fact]
    public void ToXml_preserves_xml_comment_and_declaration()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        LimitsUserXml.AddGroup(doc, LimitsKind.Usage, "Extra", new[] { "Coast" });
        var xml = LimitsUserXml.ToXml(doc);
        // Comment inside usageflags section is preserved.
        xml.Should().Contain("village combo");
        xml.Should().StartWith("<?xml");
    }

    [Fact]
    public void ToXml_round_trip_all_original_groups_survive()
    {
        var doc = LimitsUserXml.ParseDoc(Fixture);
        // No edits — just round-trip.
        var groups = LimitsUserXml.Parse(LimitsUserXml.ToXml(doc));
        groups.Select(g => g.Name).Should().BeEquivalentTo("TownVillage", "UrbanArea", "Tier123");
    }
}
