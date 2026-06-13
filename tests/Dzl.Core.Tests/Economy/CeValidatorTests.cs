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
