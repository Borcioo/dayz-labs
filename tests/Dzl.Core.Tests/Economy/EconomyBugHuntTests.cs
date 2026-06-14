using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>
/// Regression guards from an adversarial CE bug-hunt (2026-06-14). Each fact asserts the behaviour the
/// code's OWN docstring/contract promises and FAILED before the fix, demonstrating a real defect; all pass
/// once the corresponding fix is in place.
///
/// Dominant root cause that was fixed: the read path trims the <c>name</c> attribute (Parse/ReadType call
/// .Trim()) while the write path (<see cref="CeXml"/>.ByName) compared the raw attribute, so the read and
/// write halves disagreed on a preset/type/group's identity. ByName/RenameByName now trim both sides.
/// </summary>
public class EconomyBugHuntTests
{
    // ── BUG 1 (HIGH, gameplay): a <type> with no <flags> defaults to count_in_map ON
    //    (TypeFlags.CountInMap = true; DayZ treats a missing count_in_map as 1). Reading then re-saving such
    //    a type — extremely common for ammo/attachments — must NOT silently drop it from the map economy.
    [Fact]
    public void Bug1_flagsless_type_must_keep_count_in_map_on_through_a_save()
    {
        const string xml = "<types><type name=\"Ammo_556x45\"><nominal>0</nominal><lifetime>14400</lifetime></type></types>";

        var entry = TypesXml.Parse(xml).Single();
        entry.Flags.CountInMap.Should().BeTrue("a flags-less <type> counts in the map economy by default");

        var doc = TypesXml.ParseDoc(xml);
        TypesXml.Upsert(doc, entry with { Nominal = 50 });
        var outXml = TypesXml.ToXml(doc);

        outXml.Should().NotContain("count_in_map=\"0\"", "touching nominal must not disable map-counting");
        TypesXml.Parse(outXml).Single().Flags.CountInMap.Should().BeTrue();
    }

    // ── BUG 2 (HIGH, shared): trim asymmetry — Parse trims the name, ByName does not, so an edit addressed
    //    by the name the UI was given silently no-ops.
    [Fact]
    public void Bug2_edit_by_displayed_name_must_work_when_name_attr_has_trailing_space()
    {
        const string xml = "<randompresets><cargo chance=\"0.1\" name=\"food \"><item name=\"Apple\" chance=\"0.1\"/></cargo></randompresets>";
        RandomPresetsXml.Parse(xml).Single().Name.Should().Be("food", "Parse trims the name, so the UI shows 'food'");

        var doc = RandomPresetsXml.ParseDoc(xml);
        RandomPresetsXml.SetPresetChance(doc, PresetKind.Cargo, "food", 0.9)
            .Should().BeTrue("an edit by the name Parse surfaced should locate the preset");
    }

    // ── BUG 2b: the same asymmetry corrupts data — Upsert of a parsed entry whose on-disk name is padded
    //    fails to match and APPENDS a duplicate instead of updating in place.
    [Fact]
    public void Bug2b_Types_Upsert_of_a_parsed_entry_must_update_in_place_not_duplicate()
    {
        const string xml = "<types><type name=\" Foo \"><nominal>1</nominal></type></types>";
        var entry = TypesXml.Parse(xml).Single();           // Name == "Foo"

        var doc = TypesXml.ParseDoc(xml);
        TypesXml.Upsert(doc, entry with { Nominal = 2 });
        TypesXml.Parse(TypesXml.ToXml(doc))
            .Should().ContainSingle("Upsert of a parsed entry must update in place, not append a twin");
    }

    // ── BUG 3 (MED): disabled-twin namespace gap — AddPreset only scans live elements, so it recreates a
    //    name already held by a disabled (commented-out) preset, yielding a live+disabled duplicate.
    [Fact]
    public void Bug3_AddPreset_must_reject_a_name_held_by_a_disabled_twin()
    {
        const string xml = "<randompresets><cargo chance=\"0.15\" name=\"foodHermit\"><item name=\"Apple\" chance=\"0.1\"/></cargo></randompresets>";
        var doc = RandomPresetsXml.ParseDoc(xml);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();
        RandomPresetsXml.AddPreset(doc, PresetKind.Cargo, "foodHermit", 0.5)
            .Should().BeFalse("a cargo preset named foodHermit already exists (disabled)");

        RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc))
            .Count(p => p.Kind == PresetKind.Cargo && p.Name == "foodHermit")
            .Should().Be(1, "the file must not hold both a live and a disabled foodHermit");
    }

    // ── BUG 4 (LOW): CeXml.Serialize emits only the root element, dropping any comment/PI after it. A
    //    load→save round-trip with no edits must preserve the whole document.
    [Fact]
    public void Bug4_serialize_must_preserve_a_comment_after_the_root_element()
    {
        const string xml = "<?xml version=\"1.0\"?>\n<types><type name=\"x\"/></types>\n<!-- trailing -->";
        TypesXml.ToXml(TypesXml.ParseDoc(xml))
            .Should().Contain("trailing", "a no-edit round-trip must not drop a post-root comment");
    }

    // ── BUG 5 (LOW): LimitsUserXml.RemoveGroup calls GetOrCreateUserLists before checking, so a failed
    //    remove injects a phantom empty <user_lists/>. A no-op must leave the document byte-identical.
    [Fact]
    public void Bug5_RemoveGroup_must_not_mutate_the_document_on_a_noop()
    {
        var doc = LimitsUserXml.ParseDoc("<root><other/></root>");
        var before = LimitsUserXml.ToXml(doc);

        LimitsUserXml.RemoveGroup(doc, LimitsKind.Usage, "Nope").Should().BeFalse();
        LimitsUserXml.ToXml(doc).Should().Be(before, "a failed remove must not add an empty <user_lists/>");
    }

    // ── BUG 6 (MED): GlobalsXml.SetVar is documented to update type+value in place, but it rewrites the
    //    stored name's casing to whatever the (case-insensitively matched) caller passed.
    [Fact]
    public void Bug6_SetVar_value_edit_must_preserve_the_stored_name_casing()
    {
        var doc = GlobalsXml.ParseDoc("<variables><var name=\"AnimalMaxCount\" type=\"0\" value=\"200\"/></variables>");
        GlobalsXml.SetVar(doc, "animalmaxcount", 0, "300");   // case-insensitive match, value-only intent

        GlobalsXml.Parse(GlobalsXml.ToXml(doc)).Single().Name
            .Should().Be("AnimalMaxCount", "a value edit must not silently re-case the engine-consumed name");
    }

    // ── BUG 7 (MED): EventsXml.SetChild gates the type rewrite on an OrdinalIgnoreCase compare, so a
    //    case-only retype (a legitimate user fix) is silently dropped while the method returns true.
    [Fact]
    public void Bug7_SetChild_must_persist_a_case_only_type_rename()
    {
        const string xml = "<events><event name=\"AmbientFox\"><children>" +
                           "<child type=\"Animal_VulpesVulpes\" min=\"1\" max=\"2\" lootmin=\"0\" lootmax=\"0\"/>" +
                           "</children></event></events>";
        var doc = EventsXml.ParseDoc(xml);

        EventsXml.SetChild(doc, "AmbientFox", "Animal_VulpesVulpes",
            new EventChild("animal_vulpesvulpes", 1, 2, 0, 0)).Should().BeTrue();

        EventsXml.Parse(EventsXml.ToXml(doc)).Single().Children.Single().Type
            .Should().Be("animal_vulpesvulpes", "a case-only retype is a real edit that must persist");
    }

    // ── BUG 3 family: SetPresetKind / EnablePreset / RenamePreset must also honour the disabled-twin namespace.
    [Fact]
    public void Bug3b_SetPresetKind_must_reject_a_target_name_held_by_a_disabled_twin()
    {
        const string xml = "<randompresets>" +
            "<attachments chance=\"0.2\" name=\"dup\"/>" +
            "<cargo chance=\"0.3\" name=\"dup\"/>" +
            "</randompresets>";
        var doc = RandomPresetsXml.ParseDoc(xml);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Attachments, "dup").Should().BeTrue();
        RandomPresetsXml.SetPresetKind(doc, PresetKind.Cargo, "dup", PresetKind.Attachments)
            .Should().BeFalse("an attachments 'dup' already exists (disabled)");

        RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc))
            .Count(p => p.Kind == PresetKind.Attachments && p.Name == "dup").Should().Be(1);
    }

    [Fact]
    public void Bug3c_EnablePreset_must_not_resurrect_onto_a_live_twin()
    {
        const string xml = "<randompresets><cargo chance=\"0.15\" name=\"foodHermit\"/></randompresets>";
        var doc = RandomPresetsXml.ParseDoc(xml);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "foodHermit").Should().BeTrue();
        // A live foodHermit reappears (e.g. hand-edited or via the now-guarded AddPreset on older data).
        doc.Root!.Add(new System.Xml.Linq.XElement("cargo",
            new System.Xml.Linq.XAttribute("chance", "0.9"),
            new System.Xml.Linq.XAttribute("name", "foodHermit")));

        RandomPresetsXml.EnablePreset(doc, PresetKind.Cargo, "foodHermit")
            .Should().BeFalse("enabling onto an existing live twin would create two active foodHermit");
        RandomPresetsXml.Parse(RandomPresetsXml.ToXml(doc))
            .Count(p => p.Kind == PresetKind.Cargo && p.Name == "foodHermit" && !p.Disabled).Should().Be(1);
    }

    [Fact]
    public void Bug3d_RenamePreset_must_reject_a_name_held_by_a_disabled_twin()
    {
        const string xml = "<randompresets>" +
            "<cargo chance=\"0.1\" name=\"alpha\"/>" +
            "<cargo chance=\"0.2\" name=\"beta\"/>" +
            "</randompresets>";
        var doc = RandomPresetsXml.ParseDoc(xml);

        RandomPresetsXml.DisablePreset(doc, PresetKind.Cargo, "beta").Should().BeTrue();
        RandomPresetsXml.RenamePreset(doc, PresetKind.Cargo, "alpha", "beta")
            .Should().BeFalse("a disabled cargo 'beta' already occupies the name");
    }

    // ── Cascade of the root ByName trim-fix into the other CE editors (same shared helper).
    [Fact]
    public void Bug8_SpawnableTypes_AddItem_must_reject_a_whitespace_padded_duplicate()
    {
        const string xml = "<spawnabletypes><type name=\"X\"><cargo chance=\"0.5\">" +
                           "<item name=\"Foo\" chance=\"1.0\"/></cargo></type></spawnabletypes>";
        var doc = SpawnableTypesXml.ParseDoc(xml);

        SpawnableTypesXml.AddItem(doc, "X", false, 0, " Foo ", 0.3)
            .Should().BeFalse("' Foo ' is the already-present 'Foo' once trimmed");
    }

    [Fact]
    public void Bug9_PlayerSpawns_RemoveGroup_must_match_a_whitespace_padded_group()
    {
        const string xml = "<playerspawnpoints><fresh><generator_posbubbles>" +
                           "<group name=\" West \"><pos x=\"1\" z=\"2\"/></group>" +
                           "</generator_posbubbles></fresh></playerspawnpoints>";
        var doc = PlayerSpawnsXml.ParseDoc(xml);

        PlayerSpawnsXml.RemoveGroup(doc, "fresh", "generator_posbubbles", "West")
            .Should().BeTrue("Parse surfaces the group as 'West', so RemoveGroup('West') must find it");
    }
}
