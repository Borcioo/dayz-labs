using System.Globalization;
using System.Xml.Linq;
using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Tests for RandomPresetsXml: parse (invariant doubles), preset add/remove/rename/chance,
/// item add/remove/set, and round-trip preservation of comments + sibling presets.</summary>
public class RandomPresetsXmlTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <randompresets>
          <!-- keep me: hermit food drops -->
          <cargo chance="0.15" name="foodHermit">
            <item name="TunaCan" chance="0.11" />
            <item name="Apple" chance="0.07" />
          </cargo>
          <attachments chance="0.10" name="optics">
            <item name="ACOGOptic" chance="1.00" />
          </attachments>
        </randompresets>
        """;

    // ------------------------------------------------------------------
    // Parse
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_reads_cargo_and_attachments_with_invariant_doubles()
    {
        var presets = RandomPresetsXml.Parse(Fixture);

        presets.Should().HaveCount(2);

        var cargo = presets.Single(p => p.Kind == PresetKind.Cargo);
        cargo.Name.Should().Be("foodHermit");
        cargo.Chance.Should().BeApproximately(0.15, 1e-9);
        cargo.Items.Should().HaveCount(2);
        cargo.Items[0].Should().Be(new PresetItem("TunaCan", 0.11));
        cargo.Items[1].Should().Be(new PresetItem("Apple", 0.07));

        var att = presets.Single(p => p.Kind == PresetKind.Attachments);
        att.Name.Should().Be("optics");
        att.Chance.Should().BeApproximately(0.10, 1e-9);
        att.Items.Should().ContainSingle().Which.Should().Be(new PresetItem("ACOGOptic", 1.00));
    }

    [Fact]
    public void Parse_uses_invariant_culture_regardless_of_thread_culture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            // German uses comma as decimal separator — invariant parsing must ignore that.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var presets = RandomPresetsXml.Parse(Fixture);
            presets.Single(p => p.Kind == PresetKind.Cargo).Chance.Should().BeApproximately(0.15, 1e-9);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void Parse_returns_empty_on_malformed_xml()
    {
        RandomPresetsXml.Parse("<randompresets><cargo").Should().BeEmpty();
        RandomPresetsXml.Parse("").Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Preset add / remove / rename / chance
    // ------------------------------------------------------------------

    [Fact]
    public void AddPreset_adds_and_rejects_duplicate()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.AddPreset(doc, PresetKind.Cargo, "ammoStash", 0.25).Should().BeTrue();
        RandomPresetsXml.AddPreset(doc, PresetKind.Cargo, "foodHermit", 0.5).Should().BeFalse();

        var presets = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc));
        var added = presets.Single(p => p.Name == "ammoStash");
        added.Kind.Should().Be(PresetKind.Cargo);
        added.Chance.Should().BeApproximately(0.25, 1e-9);
        added.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddPreset_same_name_different_kind_is_allowed()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);
        RandomPresetsXml.AddPreset(doc, PresetKind.Attachments, "foodHermit", 0.3).Should().BeTrue();

        var presets = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc));
        presets.Count(p => p.Name == "foodHermit").Should().Be(2);
    }

    [Fact]
    public void RemovePreset_removes_and_reports_missing()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.RemovePreset(doc, PresetKind.Attachments, "optics").Should().BeTrue();
        RandomPresetsXml.RemovePreset(doc, PresetKind.Cargo, "nope").Should().BeFalse();

        var presets = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc));
        presets.Should().ContainSingle().Which.Name.Should().Be("foodHermit");
    }

    [Fact]
    public void RenamePreset_renames_and_rejects_clash()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);
        RandomPresetsXml.AddPreset(doc, PresetKind.Cargo, "other", 0.1);

        RandomPresetsXml.RenamePreset(doc, PresetKind.Cargo, "foodHermit", "foodHermit2").Should().BeTrue();
        RandomPresetsXml.RenamePreset(doc, PresetKind.Cargo, "foodHermit2", "other").Should().BeFalse();

        var presets = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc));
        presets.Should().Contain(p => p.Name == "foodHermit2");
        presets.Single(p => p.Name == "foodHermit2").Items.Should().HaveCount(2); // items preserved
    }

    [Fact]
    public void SetPresetChance_updates_value()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);
        RandomPresetsXml.SetPresetChance(doc, PresetKind.Cargo, "foodHermit", 0.99).Should().BeTrue();
        RandomPresetsXml.SetPresetChance(doc, PresetKind.Cargo, "missing", 0.5).Should().BeFalse();

        RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc))
            .Single(p => p.Name == "foodHermit").Chance.Should().BeApproximately(0.99, 1e-9);
    }

    // ------------------------------------------------------------------
    // Item add / remove / set
    // ------------------------------------------------------------------

    [Fact]
    public void AddItem_adds_and_rejects_duplicate_or_missing_preset()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.AddItem(doc, PresetKind.Cargo, "foodHermit", "Pear", 0.05).Should().BeTrue();
        RandomPresetsXml.AddItem(doc, PresetKind.Cargo, "foodHermit", "TunaCan", 0.5).Should().BeFalse();
        RandomPresetsXml.AddItem(doc, PresetKind.Cargo, "noSuch", "X", 0.5).Should().BeFalse();

        var cargo = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc)).Single(p => p.Name == "foodHermit");
        cargo.Items.Should().HaveCount(3);
        cargo.Items.Should().Contain(new PresetItem("Pear", 0.05));
    }

    [Fact]
    public void RemoveItem_removes_and_reports_missing()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.RemoveItem(doc, PresetKind.Cargo, "foodHermit", "Apple").Should().BeTrue();
        RandomPresetsXml.RemoveItem(doc, PresetKind.Cargo, "foodHermit", "Apple").Should().BeFalse();

        RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc))
            .Single(p => p.Name == "foodHermit").Items
            .Should().ContainSingle().Which.Name.Should().Be("TunaCan");
    }

    [Fact]
    public void SetItem_updates_chance_and_renames()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.SetItem(doc, PresetKind.Cargo, "foodHermit", "TunaCan", 0.42, "BakedBeans").Should().BeTrue();
        RandomPresetsXml.SetItem(doc, PresetKind.Cargo, "foodHermit", "Apple", 0.2, "BakedBeans").Should().BeFalse(); // clash
        RandomPresetsXml.SetItem(doc, PresetKind.Cargo, "foodHermit", "ghost", 0.1).Should().BeFalse();

        var cargo = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc)).Single(p => p.Name == "foodHermit");
        cargo.Items.Should().Contain(new PresetItem("BakedBeans", 0.42));
        cargo.Items.Should().NotContain(i => i.Name == "TunaCan");
    }

    // ------------------------------------------------------------------
    // Disable / enable (comment toggle)
    // ------------------------------------------------------------------

    [Fact]
    public void DisablePreset_comments_it_out_and_parse_surfaces_it_as_disabled()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();

        var xml = RandomPresetsXml.ToXml(doc);
        // The element is commented, not removed — wrapped in <!-- … --> so the game won't load it.
        xml.Should().Contain("<!-- <cargo");
        xml.Should().Contain("name=\"foodHermit\""); // still present, inside the comment

        var presets = RandomPresetsXml.Parse(xml);
        var disabled = presets.Single(p => p.Name == "foodHermit");
        disabled.Disabled.Should().BeTrue();
        disabled.Items.Should().HaveCount(2);  // items still parsed from the comment
        presets.Single(p => p.Name == "optics").Disabled.Should().BeFalse();
    }

    [Fact]
    public void DisablePreset_then_EnablePreset_round_trips_to_a_live_element()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();
        RandomPresetsXml.EnablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();

        var presets = RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc));
        var revived = presets.Single(p => p.Name == "foodHermit");
        revived.Disabled.Should().BeFalse();
        revived.Items.Should().HaveCount(2);
        revived.Chance.Should().BeApproximately(0.15, 1e-9);
    }

    [Fact]
    public void DisablePreset_is_idempotent_and_Enable_reports_missing()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();
        // Already disabled (no live element to comment) → false.
        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeFalse();
        // Enabling a preset that was never disabled → false.
        RandomPresetsXml.EnablePreset(doc, PresetKind.Attachments, "optics").Should().BeFalse();
    }

    [Fact]
    public void RemovePreset_removes_a_disabled_preset_too()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();
        RandomPresetsXml.RemovePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();

        var xml = RandomPresetsXml.ToXml(doc);
        xml.Should().NotContain("foodHermit");
        RandomPresetsXml.Parse(xml).Should().ContainSingle().Which.Name.Should().Be("optics");
    }

    [Fact]
    public void DisablePreset_preserves_unrelated_human_comments()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);
        RandomPresetsXml.DisablePreset(doc, PresetKind.Attachments, "optics").Should().BeTrue();

        var xml = RandomPresetsXml.ToXml(doc);
        // The human note must NOT be treated as a disabled preset, and must survive.
        xml.Should().Contain("<!-- keep me: hermit food drops -->");
        RandomPresetsXml.Parse(xml).Should().NotContain(p => p.Name == "" && p.Disabled);
    }

    // ------------------------------------------------------------------
    // Round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_preserves_comment_and_sibling_presets()
    {
        var doc = RandomPresetsXml.ParseDoc(Fixture);
        // Touch only the cargo preset's items; attachments + comment must survive untouched.
        RandomPresetsXml.AddItem(doc, PresetKind.Cargo, "foodHermit", "Pear", 0.05);

        var xml = RandomPresetsXml.ToXml(doc);

        xml.Should().Contain("<!-- keep me: hermit food drops -->");
        xml.Should().Contain("name=\"optics\"");
        xml.Should().Contain("name=\"ACOGOptic\"");
        // Sibling attachments preset intact after a cargo-only edit.
        var att = RandomPresetsXml.Parse(xml).Single(p => p.Kind == PresetKind.Attachments);
        att.Items.Should().ContainSingle().Which.Name.Should().Be("ACOGOptic");
    }

    [Fact]
    public void ToXml_preserves_declaration_and_handles_null_declaration()
    {
        var withDecl = RandomPresetsXml.ToXml(RandomPresetsXml.ParseDoc(Fixture));
        withDecl.Should().StartWith("<?xml");

        // A document built without a declaration should serialize root-only (no leading blank line).
        var doc = new XDocument(new XElement("randompresets"));
        var noDecl = RandomPresetsXml.ToXml(doc);
        noDecl.Should().StartWith("<randompresets");
    }
}
