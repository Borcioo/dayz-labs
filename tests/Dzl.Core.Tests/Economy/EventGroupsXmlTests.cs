using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of cfgeventgroups.xml (named object groups + children).</summary>
public class EventGroupsXmlTests
{
    private const string Xml = """
        <eventgroupdef>
          <group name="Train_Cherno">
            <child type="Wreck_A" deloot="0" lootmax="3" lootmin="1" x="0" z="0" a="78" y="1.9"/>
            <child type="Wreck_B" deloot="1" lootmax="5" lootmin="2" x="12.085" z="2.74" a="256" y="1.7"/>
          </group>
        </eventgroupdef>
        """;

    [Fact]
    public void Parse_reads_groups_and_children()
    {
        var g = EventGroupsXml.Parse(Xml).Single(x => x.Name == "Train_Cherno");
        g.Children.Should().HaveCount(2);
        var b = g.Children[1];
        b.Type.Should().Be("Wreck_B");
        b.X.Should().Be(12.085);
        b.LootMax.Should().Be(5);
        b.Deloot.Should().BeTrue();
    }

    [Fact]
    public void AddGroup_rejects_dup_AddChild_requires_type()
    {
        var doc = EventGroupsXml.ParseDoc(Xml);
        EventGroupsXml.AddGroup(doc, "New").Should().BeTrue();
        EventGroupsXml.AddGroup(doc, "train_cherno").Should().BeFalse("case-insensitive duplicate");
        EventGroupsXml.AddChild(doc, "New", "", 0, 0, 0, 0, 0, 1, false).Should().BeFalse("blank type");
        EventGroupsXml.AddChild(doc, "New", "Box", 0, 0, 0, 0, 0, 1, false).Should().BeTrue();
    }

    [Fact]
    public void SetChild_and_RemoveChild()
    {
        var doc = EventGroupsXml.ParseDoc(Xml);
        EventGroupsXml.SetChild(doc, "Train_Cherno", 0, "Wreck_A", 5, 6, 7, 8, lootMin: 2, lootMax: 9, deloot: true).Should().BeTrue();
        EventGroupsXml.RemoveChild(doc, "Train_Cherno", 1).Should().BeTrue();
        var g = EventGroupsXml.Parse(EventGroupsXml.ToXml(doc)).Single(x => x.Name == "Train_Cherno");
        g.Children.Should().ContainSingle();
        g.Children[0].LootMax.Should().Be(9);
        g.Children[0].Deloot.Should().BeTrue();
    }

    [Fact]
    public void Parse_is_empty_on_malformed()
    {
        EventGroupsXml.Parse("garbage").Should().BeEmpty();
    }
}
