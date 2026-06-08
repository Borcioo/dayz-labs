using Dzl.Core.Economy;
using FluentAssertions;
using Xunit;

/// <summary>Tests for the in-place edit methods on LimitsXml (AddName/RemoveName/RenameName/ToXml).</summary>
public class LimitsXmlEditTests
{
    // Realistic fixture XML with all four containers and an XML comment inside the root.
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <lists>
          <!-- base CE name lists — do not remove this comment -->
          <categories>
            <category name="weapons"/>
            <category name="food"/>
            <category name="containers"/>
          </categories>
          <tags>
            <tag name="floor"/>
            <tag name="shelves"/>
          </tags>
          <usageflags>
            <usage name="Military"/>
            <usage name="Police"/>
            <usage name="Town"/>
          </usageflags>
          <valueflags>
            <value name="Tier1"/>
            <value name="Tier2"/>
          </valueflags>
        </lists>
        """;

    // ------------------------------------------------------------------
    // AddName
    // ------------------------------------------------------------------

    [Fact]
    public void AddName_Usage_adds_new_entry()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        var result = LimitsXml.AddName(doc, LimitsKind.Usage, "Village");
        result.Should().BeTrue();
        var def = LimitsXml.Parse(LimitsXml.ToXml(doc));
        def.Usage.Should().Contain("Village");
    }

    [Fact]
    public void AddName_Value_adds_new_entry()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.AddName(doc, LimitsKind.Value, "Tier3").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Value.Should().Contain("Tier3");
    }

    [Fact]
    public void AddName_Tag_adds_new_entry()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.AddName(doc, LimitsKind.Tag, "roof").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Tag.Should().Contain("roof");
    }

    [Fact]
    public void AddName_Category_adds_new_entry()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.AddName(doc, LimitsKind.Category, "ammo").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Category.Should().Contain("ammo");
    }

    [Fact]
    public void AddName_is_no_op_when_name_exists_same_case()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.AddName(doc, LimitsKind.Usage, "Military").Should().BeFalse();
        // Count should remain the same.
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Usage.Count.Should().Be(3);
    }

    [Fact]
    public void AddName_is_no_op_case_insensitive()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.AddName(doc, LimitsKind.Usage, "military").Should().BeFalse();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Usage.Count.Should().Be(3);
    }

    [Fact]
    public void AddName_creates_container_when_absent()
    {
        // A minimal document with no usageflags container.
        var doc = LimitsXml.ParseDoc("<lists><categories/></lists>");
        LimitsXml.AddName(doc, LimitsKind.Usage, "Military").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Usage.Should().Contain("Military");
    }

    // ------------------------------------------------------------------
    // RemoveName
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveName_removes_existing_entry()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.RemoveName(doc, LimitsKind.Usage, "Police").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Usage.Should().NotContain("Police");
    }

    [Fact]
    public void RemoveName_is_no_op_for_unknown_name()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.RemoveName(doc, LimitsKind.Usage, "Nonexistent").Should().BeFalse();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Usage.Count.Should().Be(3);
    }

    [Fact]
    public void RemoveName_is_case_insensitive()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.RemoveName(doc, LimitsKind.Value, "tier1").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Value.Should().NotContain("Tier1");
    }

    // ------------------------------------------------------------------
    // RenameName
    // ------------------------------------------------------------------

    [Fact]
    public void RenameName_renames_entry_in_place()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.RenameName(doc, LimitsKind.Category, "weapons", "firearms").Should().BeTrue();
        var def = LimitsXml.Parse(LimitsXml.ToXml(doc));
        def.Category.Should().Contain("firearms");
        def.Category.Should().NotContain("weapons");
    }

    [Fact]
    public void RenameName_returns_false_for_unknown_old_name()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.RenameName(doc, LimitsKind.Category, "nope", "firearms").Should().BeFalse();
    }

    [Fact]
    public void RenameName_returns_false_when_new_name_already_exists()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        // "food" already exists → rename "weapons" to "food" must fail.
        LimitsXml.RenameName(doc, LimitsKind.Category, "weapons", "food").Should().BeFalse();
        // Original state unchanged.
        var def = LimitsXml.Parse(LimitsXml.ToXml(doc));
        def.Category.Should().Contain("weapons");
    }

    [Fact]
    public void RenameName_case_insensitive_old_lookup()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        LimitsXml.RenameName(doc, LimitsKind.Tag, "FLOOR", "ground").Should().BeTrue();
        LimitsXml.Parse(LimitsXml.ToXml(doc)).Tag.Should().Contain("ground");
    }

    // ------------------------------------------------------------------
    // ToXml round-trip preserves comments and unrelated entries
    // ------------------------------------------------------------------

    [Fact]
    public void ToXml_preserves_xml_comment_and_unrelated_entries()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        // Only mutate usageflags — other sections and the comment must survive.
        LimitsXml.AddName(doc, LimitsKind.Usage, "Village");
        var xml = LimitsXml.ToXml(doc);

        // Comment preserved (it lives inside the root element).
        xml.Should().Contain("do not remove this comment");
        // Unrelated categories untouched.
        xml.Should().Contain("weapons");
        xml.Should().Contain("food");
        xml.Should().Contain("containers");
        // Unrelated tags untouched.
        xml.Should().Contain("floor");
        xml.Should().Contain("shelves");
        // Added entry present.
        xml.Should().Contain("Village");
    }

    [Fact]
    public void ToXml_preserves_xml_declaration()
    {
        var doc = LimitsXml.ParseDoc(Fixture);
        var xml = LimitsXml.ToXml(doc);
        xml.Should().StartWith("<?xml");
    }

    // ------------------------------------------------------------------
    // Fix #1 — RemoveName / RenameName are non-mutating when container absent
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveName_absent_container_returns_false_and_does_not_add_empty_container()
    {
        // Doc has no usageflags container.
        const string NoUsage = "<lists><categories><category name=\"weapons\"/></categories></lists>";
        var doc = LimitsXml.ParseDoc(NoUsage);
        var result = LimitsXml.RemoveName(doc, LimitsKind.Usage, "Military");
        result.Should().BeFalse();
        // ToXml must not contain a newly-created empty <usageflags/>.
        LimitsXml.ToXml(doc).Should().NotContain("usageflags");
    }

    [Fact]
    public void RenameName_absent_container_returns_false_and_does_not_add_empty_container()
    {
        // Doc has no tags container.
        const string NoTags = "<lists><categories><category name=\"weapons\"/></categories></lists>";
        var doc = LimitsXml.ParseDoc(NoTags);
        var result = LimitsXml.RenameName(doc, LimitsKind.Tag, "floor", "ground");
        result.Should().BeFalse();
        LimitsXml.ToXml(doc).Should().NotContain("tags");
    }

    // ------------------------------------------------------------------
    // Fix #2 — ToXml does not emit a leading newline when no declaration
    // ------------------------------------------------------------------

    [Fact]
    public void ToXml_no_declaration_starts_with_angle_bracket_not_newline()
    {
        // Parse XML that has no <?xml ...?> declaration.
        var doc = LimitsXml.ParseDoc("<lists><usageflags><usage name=\"Military\"/></usageflags></lists>");
        doc.Declaration.Should().BeNull();
        var xml = LimitsXml.ToXml(doc);
        xml.Should().StartWith("<");
        xml[0].Should().Be('<');
    }
}
