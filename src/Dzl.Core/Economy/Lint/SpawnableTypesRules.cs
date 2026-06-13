namespace Dzl.Core.Economy.Lint;

/// <summary>Cross-file checks for cfgspawnabletypes.xml: every <c>preset=</c> reference must resolve
/// to a random preset of the same kind (cargo→cargo, attachments→attachments); the type name should
/// exist in types.xml; chances and damage stay in [0,1]. (Inline item classnames are not checked —
/// that needs a class DB, which is out of scope.)</summary>
public sealed class SpawnableTypesRules : ICeWorldRule
{
    public RuleScope Scope => new(CrossFile: true, CeKind.SpawnableTypes);

    public IEnumerable<LintFinding> Check(CeWorld world)
    {
        var cargoPresets = PresetNames(world, PresetKind.Cargo);
        var attachPresets = PresetNames(world, PresetKind.Attachments);
        var file = world.FileNameOf(CeKind.SpawnableTypes);

        foreach (var t in world.SpawnableTypes)
        {
            if (world.TypeNames.Count > 0 && t.Name.Length > 0 && !world.TypeNames.Contains(t.Name))
                yield return new(LintSeverity.Warning, "spawn-type-not-in-types",
                    $"'{t.Name}' has no entry in types.xml", file, t.Name, "name", CeKind.SpawnableTypes, t.Name);

            foreach (var b in t.Cargo.Concat(t.Attachments))
            {
                if (b.IsPreset)
                {
                    var valid = b.IsAttachments ? attachPresets : cargoPresets;
                    if (valid.Count > 0 && !valid.Contains(b.Preset!))
                        yield return new(LintSeverity.Error, "missing-preset",
                            $"{(b.IsAttachments ? "attachments" : "cargo")} preset '{b.Preset}' not in cfgrandompresets",
                            file, t.Name, "preset", CeKind.SpawnableTypes, t.Name);
                }
                else if (b.Chance is { } c && (c < 0 || c > 1))
                    yield return new(LintSeverity.Warning, "spawn-chance-range",
                        $"chance {c} outside 0..1 on '{t.Name}'", file, t.Name, "chance", CeKind.SpawnableTypes, t.Name);
            }

            if (t.DamageMin is { } dn && t.DamageMax is { } dx && dn > dx)
                yield return new(LintSeverity.Warning, "spawn-damage-min-gt-max",
                    $"damage min ({dn}) > max ({dx})", file, t.Name, "damage", CeKind.SpawnableTypes, t.Name);
        }
    }

    private static HashSet<string> PresetNames(CeWorld world, PresetKind kind) =>
        world.RandomPresets.Where(p => p.Kind == kind).Select(p => p.Name)
             .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
