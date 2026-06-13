using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Tests for EventsXml: parse (scalars/flags/position/limit/active/children),
/// event add/remove/rename, scalar/flag/position/active setters, child add/remove/set,
/// and round-trip preservation of comments + siblings + XML declaration.</summary>
public class EventsXmlTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <events>
          <!-- keep me: fox spawner -->
          <event name="AmbientFox">
            <nominal>0</nominal><min>0</min><max>25</max>
            <lifetime>33</lifetime><restock>25</restock>
            <saferadius>0</saferadius><distanceradius>80</distanceradius><cleanupradius>120</cleanupradius>
            <flags deletable="0" init_random="0" remove_damaged="0"/>
            <position>fixed</position><limit>mixed</limit><active>1</active>
            <children>
              <child lootmax="0" lootmin="0" max="0" min="100" type="Animal_VulpesVulpes"/>
            </children>
          </event>
          <event name="AmbientWolf">
            <nominal>5</nominal><min>2</min><max>10</max>
            <lifetime>3600</lifetime><restock>1800</restock>
            <saferadius>200</saferadius><distanceradius>500</distanceradius><cleanupradius>600</cleanupradius>
            <flags deletable="1" init_random="1" remove_damaged="1"/>
            <position>random</position><limit>child</limit><active>0</active>
            <children>
              <child lootmax="2" lootmin="0" max="3" min="1" type="Animal_CanisLupus"/>
              <child lootmax="0" lootmin="0" max="1" min="0" type="Animal_CanisLupusArctic"/>
            </children>
          </event>
        </events>
        """;

    // ------------------------------------------------------------------
    // Parse: scalars
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_reads_scalar_fields_correctly()
    {
        var events = EventsXml.Parse(Fixture);
        events.Should().HaveCount(2);

        var fox = events.Single(e => e.Name == "AmbientFox");
        fox.Nominal.Should().Be(0);
        fox.Min.Should().Be(0);
        fox.Max.Should().Be(25);
        fox.Lifetime.Should().Be(33);
        fox.Restock.Should().Be(25);
        fox.SafeRadius.Should().Be(0);
        fox.DistanceRadius.Should().Be(80);
        fox.CleanupRadius.Should().Be(120);
    }

    [Fact]
    public void Parse_reads_flags_correctly()
    {
        var events = EventsXml.Parse(Fixture);

        var fox = events.Single(e => e.Name == "AmbientFox");
        fox.Deletable.Should().BeFalse();
        fox.InitRandom.Should().BeFalse();
        fox.RemoveDamaged.Should().BeFalse();

        var wolf = events.Single(e => e.Name == "AmbientWolf");
        wolf.Deletable.Should().BeTrue();
        wolf.InitRandom.Should().BeTrue();
        wolf.RemoveDamaged.Should().BeTrue();
    }

    [Fact]
    public void Parse_reads_position_limit_active_correctly()
    {
        var events = EventsXml.Parse(Fixture);

        var fox = events.Single(e => e.Name == "AmbientFox");
        fox.Position.Should().Be("fixed");
        fox.Limit.Should().Be("mixed");
        fox.Active.Should().BeTrue();

        var wolf = events.Single(e => e.Name == "AmbientWolf");
        wolf.Position.Should().Be("random");
        wolf.Limit.Should().Be("child");
        wolf.Active.Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_children_correctly()
    {
        var events = EventsXml.Parse(Fixture);

        var fox = events.Single(e => e.Name == "AmbientFox");
        fox.Children.Should().HaveCount(1);
        fox.Children[0].Type.Should().Be("Animal_VulpesVulpes");
        fox.Children[0].Min.Should().Be(100);
        fox.Children[0].Max.Should().Be(0);
        fox.Children[0].LootMin.Should().Be(0);
        fox.Children[0].LootMax.Should().Be(0);

        var wolf = events.Single(e => e.Name == "AmbientWolf");
        wolf.Children.Should().HaveCount(2);
        wolf.Children[0].Type.Should().Be("Animal_CanisLupus");
        wolf.Children[0].Min.Should().Be(1);
        wolf.Children[0].Max.Should().Be(3);
        wolf.Children[0].LootMin.Should().Be(0);
        wolf.Children[0].LootMax.Should().Be(2);
    }

    [Fact]
    public void Parse_returns_empty_on_malformed_xml()
    {
        EventsXml.Parse("<events><event").Should().BeEmpty();
        EventsXml.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Parse_defaults_missing_scalars_to_zero()
    {
        const string xml = "<events><event name=\"Sparse\"><active>1</active><children/></event></events>";
        var events = EventsXml.Parse(xml);
        events.Should().HaveCount(1);
        var e = events[0];
        e.Nominal.Should().Be(0);
        e.Min.Should().Be(0);
        e.Max.Should().Be(0);
        e.Lifetime.Should().Be(0);
        e.Restock.Should().Be(0);
        e.SafeRadius.Should().Be(0);
        e.DistanceRadius.Should().Be(0);
        e.CleanupRadius.Should().Be(0);
        e.Deletable.Should().BeFalse();
        e.InitRandom.Should().BeFalse();
        e.RemoveDamaged.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // AddEvent / RemoveEvent / RenameEvent
    // ------------------------------------------------------------------

    [Fact]
    public void AddEvent_adds_and_rejects_duplicate()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.AddEvent(doc, "AmbientDeer").Should().BeTrue();
        EventsXml.AddEvent(doc, "AmbientFox").Should().BeFalse();   // already exists

        var events = EventsXml.Parse(EventsXml.ToXml(doc));
        events.Should().HaveCount(3);
        events.Single(e => e.Name == "AmbientDeer").Should().NotBeNull();
    }

    [Fact]
    public void RemoveEvent_removes_and_reports_missing()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.RemoveEvent(doc, "AmbientFox").Should().BeTrue();
        EventsXml.RemoveEvent(doc, "AmbientFox").Should().BeFalse();

        var events = EventsXml.Parse(EventsXml.ToXml(doc));
        events.Should().HaveCount(1);
        events[0].Name.Should().Be("AmbientWolf");
    }

    [Fact]
    public void RenameEvent_renames_and_rejects_clash()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.RenameEvent(doc, "AmbientFox", "AmbientRedFox").Should().BeTrue();
        EventsXml.RenameEvent(doc, "AmbientRedFox", "AmbientWolf").Should().BeFalse();   // clash

        var events = EventsXml.Parse(EventsXml.ToXml(doc));
        events.Should().HaveCount(2);
        events.Any(e => e.Name == "AmbientRedFox").Should().BeTrue();
        events.Any(e => e.Name == "AmbientFox").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // SetScalar / SetFlag / SetPosition / SetActive / SetLimit
    // ------------------------------------------------------------------

    [Fact]
    public void SetScalar_updates_integer_field()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.SetScalar(doc, "AmbientFox", "nominal", 42).Should().BeTrue();
        EventsXml.SetScalar(doc, "AmbientFox", "distanceradius", 999).Should().BeTrue();
        EventsXml.SetScalar(doc, "Missing", "nominal", 1).Should().BeFalse();

        var fox = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientFox");
        fox.Nominal.Should().Be(42);
        fox.DistanceRadius.Should().Be(999);
    }

    [Fact]
    public void SetFlag_updates_flag_attributes()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.SetFlag(doc, "AmbientFox", "deletable", true).Should().BeTrue();
        EventsXml.SetFlag(doc, "AmbientFox", "init_random", true).Should().BeTrue();
        EventsXml.SetFlag(doc, "Missing", "deletable", true).Should().BeFalse();

        var fox = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientFox");
        fox.Deletable.Should().BeTrue();
        fox.InitRandom.Should().BeTrue();
        fox.RemoveDamaged.Should().BeFalse();   // unchanged
    }

    [Fact]
    public void SetPosition_and_SetLimit_update_string_children()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.SetPosition(doc, "AmbientFox", "random").Should().BeTrue();
        EventsXml.SetLimit(doc, "AmbientFox", "child").Should().BeTrue();

        var fox = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientFox");
        fox.Position.Should().Be("random");
        fox.Limit.Should().Be("child");
    }

    [Fact]
    public void SetActive_toggles_active_flag()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.SetActive(doc, "AmbientFox", false).Should().BeTrue();

        var fox = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientFox");
        fox.Active.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Child add / remove / set
    // ------------------------------------------------------------------

    [Fact]
    public void AddChild_adds_and_rejects_duplicate_type()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        var newChild = new EventChild("Animal_CervusElaphus", 1, 3, 0, 1);
        EventsXml.AddChild(doc, "AmbientFox", newChild).Should().BeTrue();
        EventsXml.AddChild(doc, "AmbientFox", newChild).Should().BeFalse();   // duplicate

        var fox = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientFox");
        fox.Children.Should().HaveCount(2);
        var added = fox.Children.Single(c => c.Type == "Animal_CervusElaphus");
        added.Min.Should().Be(1);
        added.Max.Should().Be(3);
        added.LootMax.Should().Be(1);
    }

    [Fact]
    public void RemoveChild_removes_and_reports_missing()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.RemoveChild(doc, "AmbientWolf", "Animal_CanisLupus").Should().BeTrue();
        EventsXml.RemoveChild(doc, "AmbientWolf", "Animal_CanisLupus").Should().BeFalse();

        var wolf = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientWolf");
        wolf.Children.Should().HaveCount(1);
        wolf.Children[0].Type.Should().Be("Animal_CanisLupusArctic");
    }

    [Fact]
    public void SetChild_updates_numeric_fields_and_can_rename_type()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        var updated = new EventChild("Animal_VulpesVulpes_Red", 5, 10, 1, 2);
        EventsXml.SetChild(doc, "AmbientFox", "Animal_VulpesVulpes", updated).Should().BeTrue();

        var fox = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientFox");
        fox.Children.Should().HaveCount(1);
        var c = fox.Children[0];
        c.Type.Should().Be("Animal_VulpesVulpes_Red");
        c.Min.Should().Be(5);
        c.Max.Should().Be(10);
        c.LootMin.Should().Be(1);
        c.LootMax.Should().Be(2);
    }

    [Fact]
    public void SetChild_rejects_rename_clash()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        // Try to rename CanisLupus to CanisLupusArctic which already exists in AmbientWolf
        var updated = new EventChild("Animal_CanisLupusArctic", 1, 2, 0, 0);
        EventsXml.SetChild(doc, "AmbientWolf", "Animal_CanisLupus", updated).Should().BeFalse();

        // Originals unchanged
        var wolf = EventsXml.Parse(EventsXml.ToXml(doc)).Single(e => e.Name == "AmbientWolf");
        wolf.Children.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------
    // Round-trip: comment + siblings + declaration preserved
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_preserves_comment_and_sibling_events_and_declaration()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        // Simple round-trip without any edit
        var xml = EventsXml.ToXml(doc);

        xml.Should().StartWith("<?xml");
        xml.Should().Contain("<!-- keep me: fox spawner -->");
        xml.Should().Contain("AmbientFox");
        xml.Should().Contain("AmbientWolf");
    }

    [Fact]
    public void AddEvent_preserves_existing_events_and_their_children()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        EventsXml.AddEvent(doc, "AmbientBear");
        var xml = EventsXml.ToXml(doc);
        var events = EventsXml.Parse(xml);

        events.Should().HaveCount(3);
        var wolf = events.Single(e => e.Name == "AmbientWolf");
        wolf.Children.Should().HaveCount(2);
    }

    [Fact]
    public void ToXml_includes_declaration_when_present()
    {
        var doc = EventsXml.ParseDoc(Fixture);
        var xml = EventsXml.ToXml(doc);
        xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    }

    [Fact]
    public void ToXml_omits_declaration_when_absent()
    {
        var doc = EventsXml.ParseDoc("<events><event name=\"X\"><active>1</active><children/></event></events>");
        var xml = EventsXml.ToXml(doc);
        xml.Should().NotStartWith("<?xml");
        xml.Should().StartWith("<events");
    }
}
