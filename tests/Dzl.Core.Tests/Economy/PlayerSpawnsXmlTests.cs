using System.Globalization;
using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

/// <summary>Tests for PlayerSpawnsXml: parse (categories/params/groups/positions with invariant doubles),
/// SetParam upsert, group add/remove/rename, pos add/remove/set, and round-trip preservation of comments,
/// sibling categories, and the XML declaration.</summary>
public class PlayerSpawnsXmlTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <playerspawnpoints>
          <!-- keep me: fresh spawns -->
          <fresh>
            <spawn_params>
              <min_dist_infected>30</min_dist_infected>
              <max_dist_static>2</max_dist_static>
            </spawn_params>
            <generator_params>
              <grid_density>4</grid_density>
              <grid_width>200</grid_width>
              <max_steepness>45</max_steepness>
            </generator_params>
            <group_params>
              <enablegroups>true</enablegroups>
              <lifetime>120</lifetime>
            </group_params>
            <generator_posbubbles>
              <group name="WestCherno">
                <pos x="6063.018555" z="1931.907227" />
                <pos x="5933.964844" z="2171.072998" />
              </group>
              <group name="EastCherno">
                <pos x="7000.0" z="2500.5" />
              </group>
            </generator_posbubbles>
          </fresh>
          <hop>
            <generator_posbubbles>
              <group name="HopA">
                <pos x="1.0" z="2.0" />
              </group>
            </generator_posbubbles>
          </hop>
          <travel />
        </playerspawnpoints>
        """;

    // ------------------------------------------------------------------
    // Parse
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_reads_categories_params_groups_and_positions()
    {
        var cats = PlayerSpawnsXml.Parse(Fixture);

        cats.Select(c => c.Name).Should().Equal("fresh", "hop", "travel");

        var fresh = cats.Single(c => c.Name == "fresh");
        fresh.SpawnParams.Should().Contain(new SpawnParam("min_dist_infected", "30"));
        fresh.SpawnParams.Should().Contain(new SpawnParam("max_dist_static", "2"));
        fresh.GeneratorParams.Should().Contain(new SpawnParam("grid_width", "200"));
        fresh.GeneratorParams.Should().HaveCount(3);
        fresh.GroupParams.Should().Contain(new SpawnParam("enablegroups", "true"));

        fresh.Bubbles.Should().ContainSingle();
        var bubbles = fresh.Bubbles[0];
        bubbles.Container.Should().Be("generator_posbubbles");
        bubbles.Groups.Select(g => g.Name).Should().Equal("WestCherno", "EastCherno");

        var west = bubbles.Groups[0];
        west.Positions.Should().HaveCount(2);
        west.Positions[0].X.Should().BeApproximately(6063.018555, 1e-6);
        west.Positions[0].Z.Should().BeApproximately(1931.907227, 1e-6);
    }

    [Fact]
    public void Parse_uses_invariant_culture_regardless_of_thread_culture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var cats = PlayerSpawnsXml.Parse(Fixture);
            cats.Single(c => c.Name == "fresh").Bubbles[0].Groups[0].Positions[0].X
                .Should().BeApproximately(6063.018555, 1e-6);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void Parse_returns_empty_on_malformed_or_empty_xml()
    {
        PlayerSpawnsXml.Parse("<playerspawnpoints><fresh").Should().BeEmpty();
        PlayerSpawnsXml.Parse("").Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // SetParam (upsert)
    // ------------------------------------------------------------------

    [Fact]
    public void SetParam_updates_existing_value()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.SetParam(doc, "fresh", "generator_params", "grid_width", "300").Should().BeTrue();

        var fresh = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc)).Single(c => c.Name == "fresh");
        fresh.GeneratorParams.Single(p => p.Name == "grid_width").Value.Should().Be("300");
    }

    [Fact]
    public void SetParam_inserts_missing_param_and_missing_section()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        // new param in an existing section
        PlayerSpawnsXml.SetParam(doc, "fresh", "spawn_params", "new_field", "9").Should().BeTrue();
        // new section under hop (which has no spawn_params)
        PlayerSpawnsXml.SetParam(doc, "hop", "spawn_params", "min_dist_player", "50").Should().BeTrue();

        var cats = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc));
        cats.Single(c => c.Name == "fresh").SpawnParams.Should().Contain(new SpawnParam("new_field", "9"));
        cats.Single(c => c.Name == "hop").SpawnParams.Should().Contain(new SpawnParam("min_dist_player", "50"));
    }

    [Fact]
    public void SetParam_returns_false_for_missing_category()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.SetParam(doc, "nope", "spawn_params", "x", "1").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Group add / remove / rename
    // ------------------------------------------------------------------

    [Fact]
    public void AddGroup_adds_and_rejects_duplicate()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.AddGroup(doc, "fresh", "generator_posbubbles", "NewGroup").Should().BeTrue();
        PlayerSpawnsXml.AddGroup(doc, "fresh", "generator_posbubbles", "WestCherno").Should().BeFalse();

        var groups = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc))
            .Single(c => c.Name == "fresh").Bubbles[0].Groups.Select(g => g.Name);
        groups.Should().Contain("NewGroup");
    }

    [Fact]
    public void AddGroup_creates_container_when_missing()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.AddGroup(doc, "travel", "generator_posbubbles", "TravelA").Should().BeTrue();

        var travel = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc)).Single(c => c.Name == "travel");
        travel.Bubbles.Should().ContainSingle();
        travel.Bubbles[0].Groups.Single().Name.Should().Be("TravelA");
    }

    [Fact]
    public void RemoveGroup_removes_and_reports_missing()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.RemoveGroup(doc, "fresh", "generator_posbubbles", "EastCherno").Should().BeTrue();
        PlayerSpawnsXml.RemoveGroup(doc, "fresh", "generator_posbubbles", "nope").Should().BeFalse();

        PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc))
            .Single(c => c.Name == "fresh").Bubbles[0].Groups.Select(g => g.Name)
            .Should().Equal("WestCherno");
    }

    [Fact]
    public void RenameGroup_renames_preserves_positions_and_rejects_clash()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.RenameGroup(doc, "fresh", "generator_posbubbles", "WestCherno", "WestRenamed").Should().BeTrue();
        PlayerSpawnsXml.RenameGroup(doc, "fresh", "generator_posbubbles", "WestRenamed", "EastCherno").Should().BeFalse();

        var grp = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc))
            .Single(c => c.Name == "fresh").Bubbles[0].Groups.Single(g => g.Name == "WestRenamed");
        grp.Positions.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------
    // Position add / remove / set
    // ------------------------------------------------------------------

    [Fact]
    public void AddPos_appends_with_invariant_doubles()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var doc = PlayerSpawnsXml.ParseDoc(Fixture);
            PlayerSpawnsXml.AddPos(doc, "fresh", "generator_posbubbles", "EastCherno", 1234.5, 6789.25).Should().BeTrue();

            var xml = PlayerSpawnsXml.ToXml(doc);
            xml.Should().Contain("x=\"1234.5\"").And.Contain("z=\"6789.25\"");

            var east = PlayerSpawnsXml.Parse(xml).Single(c => c.Name == "fresh")
                .Bubbles[0].Groups.Single(g => g.Name == "EastCherno");
            east.Positions.Should().HaveCount(2);
            east.Positions[1].X.Should().BeApproximately(1234.5, 1e-9);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void AddPos_returns_false_for_missing_group()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.AddPos(doc, "fresh", "generator_posbubbles", "nope", 1, 2).Should().BeFalse();
    }

    [Fact]
    public void RemovePos_removes_by_index_and_guards_range()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.RemovePos(doc, "fresh", "generator_posbubbles", "WestCherno", 0).Should().BeTrue();
        PlayerSpawnsXml.RemovePos(doc, "fresh", "generator_posbubbles", "WestCherno", 5).Should().BeFalse();

        var west = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc))
            .Single(c => c.Name == "fresh").Bubbles[0].Groups.Single(g => g.Name == "WestCherno");
        west.Positions.Should().ContainSingle();
        west.Positions[0].X.Should().BeApproximately(5933.964844, 1e-6); // the second one survived
    }

    [Fact]
    public void SetPos_updates_coordinates_and_guards_range()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.SetPos(doc, "fresh", "generator_posbubbles", "WestCherno", 1, 100.5, 200.25).Should().BeTrue();
        PlayerSpawnsXml.SetPos(doc, "fresh", "generator_posbubbles", "WestCherno", 9, 0, 0).Should().BeFalse();

        var west = PlayerSpawnsXml.Parse(PlayerSpawnsXml.ToXml(doc))
            .Single(c => c.Name == "fresh").Bubbles[0].Groups.Single(g => g.Name == "WestCherno");
        west.Positions[1].X.Should().BeApproximately(100.5, 1e-9);
        west.Positions[1].Z.Should().BeApproximately(200.25, 1e-9);
    }

    // ------------------------------------------------------------------
    // Round-trip preservation
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_preserves_comment_siblings_and_declaration()
    {
        var doc = PlayerSpawnsXml.ParseDoc(Fixture);
        PlayerSpawnsXml.SetParam(doc, "fresh", "generator_params", "grid_width", "250");
        var xml = PlayerSpawnsXml.ToXml(doc);

        xml.Should().StartWith("<?xml");
        xml.Should().Contain("<!-- keep me: fresh spawns -->");
        xml.Should().Contain("<hop>");      // sibling category preserved
        xml.Should().Contain("<travel");    // empty sibling category preserved
        xml.Should().Contain("HopA");       // sibling group preserved
    }

    [Fact]
    public void ToXml_without_declaration_has_no_leading_newline()
    {
        var doc = PlayerSpawnsXml.ParseDoc("<playerspawnpoints><fresh /></playerspawnpoints>");
        var xml = PlayerSpawnsXml.ToXml(doc);
        xml.Should().StartWith("<playerspawnpoints");
    }
}
