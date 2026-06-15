using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;
using FluentAssertions;
using Xunit;

namespace Dzl.Core.Tests.Economy;

public class CeValidatorTests
{
    // System.Progress<T> reports asynchronously (posts to a SyncContext/threadpool); this captures
    // the value synchronously so the test can assert it right after the call.
    private sealed class SyncProgress : IProgress<int>
    {
        public int Last;
        public void Report(int value) => Last = value;
    }

    private static TypeEntry Type(string name) => new() { Name = name, SourceFile = "types.xml" };

    private static SpawnBlock CargoPreset(string preset) =>
        new(IsAttachments: false, Preset: preset, Chance: null, Items: System.Array.Empty<PresetItem>());

    private static SpawnableType Spawn(string name, params SpawnBlock[] cargo) =>
        new(name, Hoarder: false, DamageMin: null, DamageMax: null, Cargo: cargo,
            Attachments: System.Array.Empty<SpawnBlock>());

    private static CeEvent Event(string name, int min, int max, params EventChild[] children) =>
        new(name, Nominal: 1, Min: min, Max: max, Lifetime: 0, Restock: 0, SafeRadius: 0,
            DistanceRadius: 0, CleanupRadius: 0, Deletable: false, InitRandom: false,
            RemoveDamaged: false, Position: "fixed", Limit: "mixed", Active: true, Children: children);

    // --- SpawnableTypes ↔ RandomPresets cross-file ---

    [Fact]
    public void Missing_cargo_preset_is_an_error()
    {
        var world = new CeWorld
        {
            Types = new CeFileSet(new[] { Type("Foo") }),
            SpawnableTypes = new[] { Spawn("Foo", CargoPreset("doesNotExist")) },
            // cfgrandompresets IS loaded (has cargo presets), just not the referenced one.
            RandomPresets = new[] { new RandomPreset(PresetKind.Cargo, "real", 1.0, System.Array.Empty<PresetItem>()) },
        };
        new SpawnableTypesRules().Check(world)
            .Should().Contain(f => f.Code == "missing-preset" && f.Severity == LintSeverity.Error);
    }

    [Fact]
    public void Cargo_preset_present_as_cargo_is_fine()
    {
        var world = new CeWorld
        {
            Types = new CeFileSet(new[] { Type("Foo") }),
            SpawnableTypes = new[] { Spawn("Foo", CargoPreset("mixArmy")) },
            RandomPresets = new[] { new RandomPreset(PresetKind.Cargo, "mixArmy", 1.0, System.Array.Empty<PresetItem>()) },
        };
        new SpawnableTypesRules().Check(world).Should().NotContain(f => f.Code == "missing-preset");
    }

    [Fact]
    public void Cargo_referencing_an_attachments_only_preset_is_missing()
    {
        // Right name, wrong kind: the preset exists only as attachments, so a cargo ref doesn't resolve.
        var world = new CeWorld
        {
            Types = new CeFileSet(new[] { Type("Foo") }),
            SpawnableTypes = new[] { Spawn("Foo", CargoPreset("mixMili")) },
            RandomPresets = new[]
            {
                new RandomPreset(PresetKind.Attachments, "mixMili", 1.0, System.Array.Empty<PresetItem>()),
                new RandomPreset(PresetKind.Cargo, "someCargo", 1.0, System.Array.Empty<PresetItem>()),  // cargo set non-empty
            },
        };
        new SpawnableTypesRules().Check(world).Should().Contain(f => f.Code == "missing-preset");
    }

    [Fact]
    public void Spawnable_type_absent_from_types_is_a_warning()
    {
        var world = new CeWorld
        {
            Types = new CeFileSet(new[] { Type("Other") }),
            SpawnableTypes = new[] { Spawn("Ghost", CargoPreset("p")) },
            RandomPresets = new[] { new RandomPreset(PresetKind.Cargo, "p", 1.0, System.Array.Empty<PresetItem>()) },
        };
        new SpawnableTypesRules().Check(world)
            .Should().Contain(f => f.Code == "spawn-type-not-in-types" && f.EntryName == "Ghost");
    }

    // --- Events ↔ Types cross-file ---

    [Fact]
    public void Event_child_absent_from_types_is_a_warning()
    {
        var world = new CeWorld
        {
            Types = new CeFileSet(new[] { Type("ZombieCivil") }),
            Events = new[] { Event("EventZ", 0, 10, new EventChild("NoSuchClass", 100, 1, 0, 0)) },
        };
        new EventsRules().Check(world)
            .Should().Contain(f => f.Code == "event-child-not-in-types" && f.Kind == CeKind.Events);
    }

    [Fact]
    public void Event_min_greater_than_max_is_a_warning()
    {
        var world = new CeWorld { Events = new[] { Event("EventZ", 9, 3) } };
        new EventsRules().Check(world).Should().Contain(f => f.Code == "event-min-gt-max");
    }

    [Fact]
    public void Event_with_count_range_children_does_not_flag_weight_sum()
    {
        // max>0 → min/max are a literal count range (like vanilla vehicles spawning 3-5 per variant),
        // not weights; their sum (here 6) is meaningless, so the 100-convention hint must NOT fire.
        var world = new CeWorld
        {
            Events = new[] { Event("Vehicles", 0, 10,
                new EventChild("Sedan_02", 3, 5, 0, 0),
                new EventChild("Sedan_02_Red", 3, 5, 0, 0)) },
        };
        new EventsRules().Check(world).Should().NotContain(f => f.Code == "event-children-weight-sum");
    }

    [Fact]
    public void Event_with_weight_children_not_summing_to_100_is_info()
    {
        // max=0 → min is a % spawn-weight; two weights summing to 90 (not 100) earns the Info hint.
        var world = new CeWorld
        {
            Events = new[] { Event("Spawner", 0, 10,
                new EventChild("Animal_A", 60, 0, 0, 0),
                new EventChild("Animal_B", 30, 0, 0, 0)) },
        };
        new EventsRules().Check(world).Should()
            .Contain(f => f.Code == "event-children-weight-sum" && f.Severity == LintSeverity.Info);
    }

    // --- Validator dispatch ---

    [Fact]
    public void PerFile_runs_only_that_kinds_light_rules()
    {
        var world = new CeWorld
        {
            Types = new CeFileSet(new[] { Type(""), Type("Dup"), Type("Dup") }),  // empty-name + duplicate
            SpawnableTypes = new[] { Spawn("X", CargoPreset("nope")) },           // a cross-file error
        };
        var validator = new CeValidator();

        // Types is per-file → its findings show; SpawnableTypes rule is cross-file → excluded here.
        validator.ValidatePerFile(world, CeKind.Types).Should().Contain(f => f.Code == "duplicate-name");
        validator.ValidatePerFile(world, CeKind.SpawnableTypes).Should().BeEmpty();
    }

    // --- RandomPresets / Globals / Dictionaries ---

    [Fact]
    public void Preset_chance_out_of_range_is_a_warning()
    {
        var world = new CeWorld
        {
            RandomPresets = new[] { new RandomPreset(PresetKind.Cargo, "p", 2.0, System.Array.Empty<PresetItem>()) },
        };
        new RandomPresetsRules().Check(world).Should().Contain(f => f.Code == "preset-chance-range");
    }

    [Fact]
    public void Preset_referenced_by_a_spawnabletype_is_not_flagged_unused()
    {
        var world = new CeWorld
        {
            SpawnableTypes = new[] { Spawn("Foo", CargoPreset("used")) },
            RandomPresets = new[]
            {
                new RandomPreset(PresetKind.Cargo, "used", 1.0, System.Array.Empty<PresetItem>()),
                new RandomPreset(PresetKind.Cargo, "lonely", 1.0, System.Array.Empty<PresetItem>()),
            },
        };
        var findings = new UnusedPresetRule().Check(world).ToList();
        findings.Should().Contain(f => f.Code == "unused-preset" && f.EntryName == "lonely");
        findings.Should().NotContain(f => f.EntryName == "used");
    }

    [Fact]
    public void Global_bad_type_and_non_numeric_value_are_warnings()
    {
        var world = new CeWorld { Globals = new[] { new GlobalVar("Weird", 5, "notnum") } };
        var codes = new GlobalsRules().Check(world).Select(f => f.Code).ToList();
        codes.Should().Contain("global-type-range");
        codes.Should().Contain("global-value-nan");
    }

    [Fact]
    public void User_list_member_absent_from_base_definition_is_an_error()
    {
        var world = new CeWorld
        {
            Limits = new LimitsDef(
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Town" },
                new HashSet<string>(), new HashSet<string>(), new HashSet<string>()),
            UserGroups = new[] { new LimitsUserGroup("TownNope", LimitsKind.Usage, new[] { "Town", "Nope" }) },
        };
        new DictionariesRules().Check(world)
            .Should().Contain(f => f.Code == "user-list-unknown-member" && f.Message.Contains("Nope"));
    }

    // --- Types ↔ named combos (cfglimitsdefinitionuser.xml) ---

    [Fact]
    public void Type_referencing_a_named_combo_value_is_not_flagged_unknown()
    {
        // A type may reference a named combo where a value flag is expected; the engine expands the combo to
        // its member flags. Base valueflags are Tier1–Tier4; the combo "Tier123" bundles three of them, so a
        // "<value name=\"Tier123\"/>" must validate even though Tier123 is absent from cfglimitsdefinition.
        var world = new CeWorld
        {
            Types = new CeFileSet(new[]
            {
                new TypeEntry { Name = "Gun", SourceFile = "types.xml", Value = new[] { "Tier123" } },
            }),
            Limits = new LimitsDef(
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Tier1", "Tier2", "Tier3", "Tier4" },
                new HashSet<string>(), new HashSet<string>()),
            UserGroups = new[]
            {
                new LimitsUserGroup("Tier123", LimitsKind.Value, new[] { "Tier1", "Tier2", "Tier3" }),
            },
        };

        new TypesWorldRule().Check(world).Should().NotContain(f => f.Code == "unknown-value");
    }

    [Fact]
    public void Type_referencing_an_undefined_value_is_still_flagged_unknown()
    {
        // Negative control: a value that is neither a base flag nor a combo is still flagged, so the combo
        // fold-in above doesn't silently mask the rule.
        var world = new CeWorld
        {
            Types = new CeFileSet(new[]
            {
                new TypeEntry { Name = "Gun", SourceFile = "types.xml", Value = new[] { "TierNope" } },
            }),
            Limits = new LimitsDef(
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Tier1", "Tier2" },
                new HashSet<string>(), new HashSet<string>()),
            UserGroups = new[]
            {
                new LimitsUserGroup("Tier123", LimitsKind.Value, new[] { "Tier1" }),
            },
        };

        new TypesWorldRule().Check(world).Should().Contain(f => f.Code == "unknown-value" && f.EntryName == "Gun");
    }

    [Fact]
    public void Full_pass_reports_progress_to_100_and_includes_cross_file_findings()
    {
        var world = new CeWorld
        {
            SpawnableTypes = new[] { Spawn("X", CargoPreset("nope")) },
            RandomPresets = new[] { new RandomPreset(PresetKind.Cargo, "real", 1.0, System.Array.Empty<PresetItem>()) },
        };
        var progress = new SyncProgress();
        var findings = new CeValidator().ValidateFull(world, progress);
        progress.Last.Should().Be(100);   // last rule → 100%
        findings.Should().Contain(f => f.Code == "missing-preset");
    }
}
