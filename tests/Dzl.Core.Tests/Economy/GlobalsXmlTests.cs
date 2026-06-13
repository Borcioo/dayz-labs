using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Tests for GlobalsXml: parse, SetVar (upsert), RemoveVar, RenameVar, and round-trip
/// preservation of comments, sibling vars, and the XML declaration.</summary>
public class GlobalsXmlTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <variables>
          <!-- simulation tuning -->
          <var name="AnimalMaxCount" type="0" value="200"/>
          <var name="LootDamageMax" type="1" value="0.82"/>
        </variables>
        """;

    // ------------------------------------------------------------------
    // Parse
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_reads_all_vars()
    {
        var vars = GlobalsXml.Parse(Fixture);

        vars.Should().HaveCount(2);

        var animals = vars.Single(v => v.Name == "AnimalMaxCount");
        animals.Type.Should().Be(0);
        animals.Value.Should().Be("200");

        var loot = vars.Single(v => v.Name == "LootDamageMax");
        loot.Type.Should().Be(1);
        loot.Value.Should().Be("0.82");
    }

    [Fact]
    public void Parse_empty_xml_returns_empty_list()
    {
        GlobalsXml.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Parse_malformed_xml_returns_empty_list()
    {
        GlobalsXml.Parse("<variables><var name=BROKEN").Should().BeEmpty();
    }

    [Fact]
    public void Parse_skips_var_elements_without_name()
    {
        const string xml = "<variables><var type=\"0\" value=\"42\"/><var name=\"Good\" type=\"0\" value=\"1\"/></variables>";
        var vars = GlobalsXml.Parse(xml);
        vars.Should().ContainSingle().Which.Name.Should().Be("Good");
    }

    // ------------------------------------------------------------------
    // SetVar — add (new key)
    // ------------------------------------------------------------------

    [Fact]
    public void SetVar_adds_new_var_when_name_does_not_exist()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        var result = GlobalsXml.SetVar(doc, "ZombieMaxCount", 0, "800");

        result.Should().BeTrue();
        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().HaveCount(3);
        var added = vars.Single(v => v.Name == "ZombieMaxCount");
        added.Type.Should().Be(0);
        added.Value.Should().Be("800");
    }

    [Fact]
    public void SetVar_add_preserves_existing_siblings()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.SetVar(doc, "NewVar", 1, "3.14");

        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().ContainSingle(v => v.Name == "AnimalMaxCount");
        vars.Should().ContainSingle(v => v.Name == "LootDamageMax");
    }

    // ------------------------------------------------------------------
    // SetVar — update (existing key)
    // ------------------------------------------------------------------

    [Fact]
    public void SetVar_updates_existing_var_in_place()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        var result = GlobalsXml.SetVar(doc, "AnimalMaxCount", 0, "999");

        result.Should().BeTrue();
        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        // count unchanged
        vars.Should().HaveCount(2);
        vars.Single(v => v.Name == "AnimalMaxCount").Value.Should().Be("999");
    }

    [Fact]
    public void SetVar_update_is_case_insensitive_on_name_match()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        GlobalsXml.SetVar(doc, "animalmaxcount", 0, "1");

        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        // Lowercase key matched and updated; count unchanged
        vars.Should().HaveCount(2);
        vars.Single(v => v.Name.Equals("AnimalMaxCount", StringComparison.OrdinalIgnoreCase)).Value.Should().Be("1");
    }

    [Fact]
    public void SetVar_can_change_type_when_updating()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        GlobalsXml.SetVar(doc, "LootDamageMax", 0, "1");

        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Single(v => v.Name == "LootDamageMax").Type.Should().Be(0);
    }

    [Fact]
    public void SetVar_returns_false_for_empty_name()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.SetVar(doc, "", 0, "1").Should().BeFalse();
        GlobalsXml.SetVar(doc, "   ", 0, "1").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // RemoveVar
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveVar_removes_existing_var()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        var result = GlobalsXml.RemoveVar(doc, "AnimalMaxCount");

        result.Should().BeTrue();
        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().HaveCount(1);
        vars.Should().NotContain(v => v.Name == "AnimalMaxCount");
    }

    [Fact]
    public void RemoveVar_returns_false_when_name_not_found()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        GlobalsXml.RemoveVar(doc, "DoesNotExist").Should().BeFalse();
        GlobalsXml.Parse(GlobalsXml.ToXml(doc)).Should().HaveCount(2);
    }

    [Fact]
    public void RemoveVar_preserves_sibling_vars()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.RemoveVar(doc, "AnimalMaxCount");

        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().ContainSingle(v => v.Name == "LootDamageMax");
    }

    // ------------------------------------------------------------------
    // RenameVar
    // ------------------------------------------------------------------

    [Fact]
    public void RenameVar_renames_existing_var_in_place()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);

        var result = GlobalsXml.RenameVar(doc, "AnimalMaxCount", "AnimalLimit");

        result.Should().BeTrue();
        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().HaveCount(2);
        vars.Should().ContainSingle(v => v.Name == "AnimalLimit");
        vars.Should().NotContain(v => v.Name == "AnimalMaxCount");
    }

    [Fact]
    public void RenameVar_returns_false_when_old_name_not_found()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.RenameVar(doc, "NoSuchVar", "Whatever").Should().BeFalse();
    }

    [Fact]
    public void RenameVar_returns_false_when_new_name_already_exists()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.RenameVar(doc, "AnimalMaxCount", "LootDamageMax").Should().BeFalse();
        // both still present and unchanged
        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().HaveCount(2);
    }

    [Fact]
    public void RenameVar_same_name_noop_succeeds()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.RenameVar(doc, "AnimalMaxCount", "AnimalMaxCount").Should().BeTrue();
        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().HaveCount(2);
    }

    [Fact]
    public void RenameVar_preserves_type_and_value()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.RenameVar(doc, "LootDamageMax", "LootMax");

        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        var renamed = vars.Single(v => v.Name == "LootMax");
        renamed.Type.Should().Be(1);
        renamed.Value.Should().Be("0.82");
    }

    // ------------------------------------------------------------------
    // Round-trip: comments, order, declaration
    // ------------------------------------------------------------------

    [Fact]
    public void ToXml_preserves_xml_declaration()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        var xml = GlobalsXml.ToXml(doc);
        xml.Should().StartWith("<?xml");
    }

    [Fact]
    public void ToXml_preserves_comment_after_roundtrip()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.SetVar(doc, "NewVar", 0, "0");

        var xml = GlobalsXml.ToXml(doc);
        xml.Should().Contain("simulation tuning");
    }

    [Fact]
    public void ToXml_no_declaration_returns_root_only()
    {
        const string noDecl = "<variables><var name=\"X\" type=\"0\" value=\"1\"/></variables>";
        var doc = GlobalsXml.ParseDoc(noDecl);
        var xml = GlobalsXml.ToXml(doc);
        xml.Should().StartWith("<variables");
        xml.Should().NotContain("<?xml");
    }

    [Fact]
    public void SetVar_then_RemoveVar_then_RenameVar_preserves_remaining_var()
    {
        var doc = GlobalsXml.ParseDoc(Fixture);
        GlobalsXml.SetVar(doc, "Extra", 0, "5");
        GlobalsXml.RemoveVar(doc, "Extra");
        GlobalsXml.RenameVar(doc, "AnimalMaxCount", "Animals");

        var vars = GlobalsXml.Parse(GlobalsXml.ToXml(doc));
        vars.Should().HaveCount(2);
        vars.Should().ContainSingle(v => v.Name == "Animals");
        vars.Should().ContainSingle(v => v.Name == "LootDamageMax");
    }
}
