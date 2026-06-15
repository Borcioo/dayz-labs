using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of cfgeventspawns.xml (per-event spawn positions).</summary>
public class EventSpawnsXmlTests
{
    private const string Xml = """
        <eventposdef>
          <event name="VehicleSedan">
            <pos x="100.5" z="200.25" a="90"/>
            <pos x="300" z="400" a="0"/>
          </event>
          <event name="StaticHeliCrash"/>
        </eventposdef>
        """;

    [Fact]
    public void Parse_reads_events_and_positions()
    {
        var ev = EventSpawnsXml.Parse(Xml);
        ev.Select(e => e.Name).Should().BeEquivalentTo("VehicleSedan", "StaticHeliCrash");
        var sedan = ev.Single(e => e.Name == "VehicleSedan");
        sedan.Positions.Should().HaveCount(2);
        sedan.Positions[0].X.Should().Be(100.5);
        sedan.Positions[0].A.Should().Be(90);
    }

    [Fact]
    public void AddEvent_rejects_a_duplicate()
    {
        var doc = EventSpawnsXml.ParseDoc(Xml);
        EventSpawnsXml.AddEvent(doc, "NewEvent").Should().BeTrue();
        EventSpawnsXml.AddEvent(doc, "vehiclesedan").Should().BeFalse("case-insensitive duplicate");
    }

    [Fact]
    public void AddPos_and_SetPos_and_RemovePos()
    {
        var doc = EventSpawnsXml.ParseDoc(Xml);
        EventSpawnsXml.AddPos(doc, "StaticHeliCrash", 1, 2, 3).Should().BeTrue();
        EventSpawnsXml.Parse(EventSpawnsXml.ToXml(doc)).Single(e => e.Name == "StaticHeliCrash").Positions.Should().ContainSingle();

        EventSpawnsXml.SetPos(doc, "VehicleSedan", 0, 9, 9, 9).Should().BeTrue();
        EventSpawnsXml.RemovePos(doc, "VehicleSedan", 1).Should().BeTrue();
        var sedan = EventSpawnsXml.Parse(EventSpawnsXml.ToXml(doc)).Single(e => e.Name == "VehicleSedan");
        sedan.Positions.Should().ContainSingle();
        sedan.Positions[0].X.Should().Be(9);
    }

    [Fact]
    public void Parse_is_empty_on_malformed()
    {
        EventSpawnsXml.Parse("garbage").Should().BeEmpty();
    }
}
