namespace Dzl.Core.Economy.Lint;

/// <summary>Per-file cfgrandompresets.xml checks: preset and item chances stay in [0,1].</summary>
public sealed class RandomPresetsRules : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: false, CeKind.RandomPresets);

    public IEnumerable<LintFinding> Check(CeWorld world)
    {
        var file = world.FileNameOf(CeKind.RandomPresets);
        foreach (var p in world.RandomPresets)
        {
            if (p.Chance < 0 || p.Chance > 1)
                yield return new(LintSeverity.Warning, "preset-chance-range",
                    $"preset '{p.Name}' chance {p.Chance} outside 0..1", file, p.Name, "chance", CeKind.RandomPresets, p.Name);

            foreach (var it in p.Items.Where(i => i.Chance < 0 || i.Chance > 1))
                yield return new(LintSeverity.Warning, "preset-item-chance-range",
                    $"item '{it.Name}' chance {it.Chance} outside 0..1 in preset '{p.Name}'",
                    file, p.Name, "item", CeKind.RandomPresets, p.Name);
        }
    }
}

/// <summary>Cross-file: a random preset referenced by no spawnabletype (of its kind) is dead weight.
/// Info-level — being unused is legal/common (legacy or staged presets), just worth surfacing.</summary>
public sealed class UnusedPresetRule : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: true, CeKind.RandomPresets);

    public IEnumerable<LintFinding> Check(CeWorld world)
    {
        var refdCargo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refdAttach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in world.SpawnableTypes)
            foreach (var b in t.Cargo.Concat(t.Attachments))
                if (b.IsPreset && b.Preset is { } pr)
                    (b.IsAttachments ? refdAttach : refdCargo).Add(pr);

        var file = world.FileNameOf(CeKind.RandomPresets);
        foreach (var p in world.RandomPresets)
        {
            var referenced = p.Kind == PresetKind.Attachments ? refdAttach : refdCargo;
            if (!referenced.Contains(p.Name))
                yield return new(LintSeverity.Info, "unused-preset",
                    $"preset '{p.Name}' is not referenced by any spawnabletype",
                    file, p.Name, "name", CeKind.RandomPresets, p.Name);
        }
    }
}
