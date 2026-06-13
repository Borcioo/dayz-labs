using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Tests for SpawnableTypesXml: parse (hoarder/damage/preset+chance blocks, cargo & attachments,
/// invariant doubles), type add/remove/rename, hoarder/damage edits, block add/remove + preset/chance,
/// item add/remove, and round-trip preservation of comments + sibling types.</summary>
public class SpawnableTypesXmlTests
{
    private const string Fixture = """
        <?xml version="1.0" encoding="UTF-8"?>
        <spawnabletypes>
          <!-- keep me: barrels -->
          <type name="Barrel_Blue"><hoarder /></type>
          <type name="GiftWrapPaper"><damage min="0.0" max="0.32" /></type>
          <type name="SomeVest">
            <attachments chance="0.85"><item name="PlateCarrierHolster_Camo" chance="1.00" /></attachments>
            <cargo preset="mixArmy" />
          </type>
          <type name="X"><cargo chance="0.5"><item name="Foo" chance="1.0" /></cargo></type>
        </spawnabletypes>
        """;

    // ------------------------------------------------------------------
    // Parse
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_reads_hoarder_only()
    {
        var t = SpawnableTypesXml.Parse(Fixture).Single(x => x.Name == "Barrel_Blue");
        t.Hoarder.Should().BeTrue();
        t.DamageMin.Should().BeNull();
        t.DamageMax.Should().BeNull();
        t.Cargo.Should().BeEmpty();
        t.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_reads_damage_only_with_invariant_doubles()
    {
        var t = SpawnableTypesXml.Parse(Fixture).Single(x => x.Name == "GiftWrapPaper");
        t.Hoarder.Should().BeFalse();
        t.DamageMin.Should().BeApproximately(0.0, 1e-9);
        t.DamageMax.Should().BeApproximately(0.32, 1e-9);
    }

    [Fact]
    public void Parse_reads_preset_cargo_block()
    {
        var t = SpawnableTypesXml.Parse(Fixture).Single(x => x.Name == "SomeVest");
        t.Cargo.Should().HaveCount(1);
        var cargo = t.Cargo[0];
        cargo.IsPreset.Should().BeTrue();
        cargo.Preset.Should().Be("mixArmy");
        cargo.Chance.Should().BeNull();
        cargo.Items.Should().BeEmpty();
        cargo.IsAttachments.Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_chance_attachments_block_with_items()
    {
        var t = SpawnableTypesXml.Parse(Fixture).Single(x => x.Name == "SomeVest");
        t.Attachments.Should().HaveCount(1);
        var att = t.Attachments[0];
        att.IsPreset.Should().BeFalse();
        att.Preset.Should().BeNull();
        att.Chance.Should().BeApproximately(0.85, 1e-9);
        att.IsAttachments.Should().BeTrue();
        att.Items.Should().ContainSingle()
            .Which.Should().Be(new PresetItem("PlateCarrierHolster_Camo", 1.00));
    }

    [Fact]
    public void Parse_reads_chance_cargo_block_with_items()
    {
        var t = SpawnableTypesXml.Parse(Fixture).Single(x => x.Name == "X");
        t.Cargo.Should().HaveCount(1);
        t.Cargo[0].Chance.Should().BeApproximately(0.5, 1e-9);
        t.Cargo[0].Items.Single().Should().Be(new PresetItem("Foo", 1.0));
    }

    [Fact]
    public void Parse_never_throws_on_malformed()
    {
        SpawnableTypesXml.Parse("not xml <<<").Should().BeEmpty();
        SpawnableTypesXml.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Parse_uses_invariant_culture_for_doubles()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var t = SpawnableTypesXml.Parse(Fixture).Single(x => x.Name == "GiftWrapPaper");
            t.DamageMax.Should().BeApproximately(0.32, 1e-9);
        }
        finally { Thread.CurrentThread.CurrentCulture = prev; }
    }

    // ------------------------------------------------------------------
    // Type-level edits
    // ------------------------------------------------------------------

    [Fact]
    public void AddType_appends_and_rejects_duplicate()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.AddType(doc, "NewThing").Should().BeTrue();
        SpawnableTypesXml.AddType(doc, "newthing").Should().BeFalse(); // case-insensitive dup
        var parsed = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc));
        parsed.Should().Contain(t => t.Name == "NewThing");
    }

    [Fact]
    public void RemoveType_removes_named()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.RemoveType(doc, "X").Should().BeTrue();
        SpawnableTypesXml.RemoveType(doc, "Nope").Should().BeFalse();
        SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Should().NotContain(t => t.Name == "X");
    }

    [Fact]
    public void RenameType_renames_and_rejects_clash()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.RenameType(doc, "X", "Y").Should().BeTrue();
        SpawnableTypesXml.RenameType(doc, "Y", "Barrel_Blue").Should().BeFalse(); // clash
        var parsed = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc));
        parsed.Should().Contain(t => t.Name == "Y");
        parsed.Should().NotContain(t => t.Name == "X");
    }

    [Fact]
    public void SetHoarder_sets_and_clears()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.SetHoarder(doc, "X", true).Should().BeTrue();
        SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X").Hoarder.Should().BeTrue();

        SpawnableTypesXml.SetHoarder(doc, "Barrel_Blue", false).Should().BeTrue();
        SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "Barrel_Blue").Hoarder.Should().BeFalse();

        SpawnableTypesXml.SetHoarder(doc, "Missing", true).Should().BeFalse();
    }

    [Fact]
    public void SetDamage_sets_then_clears()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.SetDamage(doc, "X", 0.1, 0.9).Should().BeTrue();
        var afterSet = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X");
        afterSet.DamageMin.Should().BeApproximately(0.1, 1e-9);
        afterSet.DamageMax.Should().BeApproximately(0.9, 1e-9);

        SpawnableTypesXml.SetDamage(doc, "X", null, null).Should().BeTrue();
        var afterClear = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X");
        afterClear.DamageMin.Should().BeNull();
        afterClear.DamageMax.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Block-level edits
    // ------------------------------------------------------------------

    [Fact]
    public void AddBlock_chance_and_preset()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.AddBlock(doc, "Barrel_Blue", isAttachments: false, preset: null, chance: 0.25).Should().Be(0);
        SpawnableTypesXml.AddBlock(doc, "Barrel_Blue", isAttachments: true, preset: "optics", chance: null).Should().Be(0);

        var t = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(x => x.Name == "Barrel_Blue");
        t.Cargo.Single().IsPreset.Should().BeFalse();
        t.Cargo.Single().Chance.Should().BeApproximately(0.25, 1e-9);
        t.Attachments.Single().Preset.Should().Be("optics");
    }

    [Fact]
    public void RemoveBlock_by_index()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.RemoveBlock(doc, "SomeVest", isAttachments: false, index: 0).Should().BeTrue();
        SpawnableTypesXml.RemoveBlock(doc, "SomeVest", isAttachments: false, index: 0).Should().BeFalse();
        var t = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(x => x.Name == "SomeVest");
        t.Cargo.Should().BeEmpty();
        t.Attachments.Should().HaveCount(1); // untouched
    }

    [Fact]
    public void SetBlockPreset_strips_chance_and_items()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.SetBlockPreset(doc, "X", isAttachments: false, index: 0, preset: "mixFood").Should().BeTrue();
        var b = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X").Cargo.Single();
        b.IsPreset.Should().BeTrue();
        b.Preset.Should().Be("mixFood");
        b.Chance.Should().BeNull();
        b.Items.Should().BeEmpty();
    }

    [Fact]
    public void SetBlockChance_strips_preset()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.SetBlockChance(doc, "SomeVest", isAttachments: false, index: 0, chance: 0.42).Should().BeTrue();
        var b = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "SomeVest").Cargo.Single();
        b.IsPreset.Should().BeFalse();
        b.Chance.Should().BeApproximately(0.42, 1e-9);
    }

    // ------------------------------------------------------------------
    // Item-level edits
    // ------------------------------------------------------------------

    [Fact]
    public void AddItem_and_RemoveItem_in_chance_block()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.AddItem(doc, "X", isAttachments: false, index: 0, "Bar", 0.3).Should().BeTrue();
        SpawnableTypesXml.AddItem(doc, "X", isAttachments: false, index: 0, "bar", 0.3).Should().BeFalse(); // dup
        var t1 = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X");
        t1.Cargo.Single().Items.Should().HaveCount(2);

        SpawnableTypesXml.RemoveItem(doc, "X", isAttachments: false, index: 0, "Foo").Should().BeTrue();
        var t2 = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X");
        t2.Cargo.Single().Items.Single().Name.Should().Be("Bar");
    }

    [Fact]
    public void SetItem_updates_chance_and_renames()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.SetItem(doc, "X", isAttachments: false, index: 0, "Foo", 0.7, "Baz").Should().BeTrue();
        var it = SpawnableTypesXml.Parse(SpawnableTypesXml.ToXml(doc)).Single(t => t.Name == "X").Cargo.Single().Items.Single();
        it.Name.Should().Be("Baz");
        it.Chance.Should().BeApproximately(0.7, 1e-9);
    }

    // ------------------------------------------------------------------
    // Round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_preserves_comment_and_siblings()
    {
        var doc = SpawnableTypesXml.ParseDoc(Fixture);
        SpawnableTypesXml.SetHoarder(doc, "X", true);
        var xml = SpawnableTypesXml.ToXml(doc);

        xml.Should().Contain("keep me: barrels");                 // comment preserved
        xml.Should().Contain("Barrel_Blue");                      // siblings preserved
        xml.Should().Contain("mixArmy");
        xml.Should().Contain("PlateCarrierHolster_Camo");
        xml.Should().StartWith("<?xml");                          // declaration preserved

        var parsed = SpawnableTypesXml.Parse(xml);
        parsed.Should().HaveCount(4);
    }

    [Fact]
    public void ToXml_is_null_declaration_safe()
    {
        var doc = SpawnableTypesXml.ParseDoc("<spawnabletypes><type name=\"A\"/></spawnabletypes>");
        var xml = SpawnableTypesXml.ToXml(doc);
        xml.Should().Contain("<type name=\"A\"");
        xml.Should().NotStartWith("\n");
    }
}
