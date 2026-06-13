namespace Dzl.Core.Economy;

/// <summary>One CE file's resolution state: which kind, its name/path, and whether it exists on disk.</summary>
public sealed record CeFileInfo(CeKind Kind, string FileName, string Path, bool Exists);

/// <summary>The loaded Central Economy of a mission: every CE file's parsed data plus which files
/// resolved. Pure container — built by <c>CeWorldLoader</c>, read by <see cref="Lint.ICeWorldRule"/>,
/// never mutated. Empty (not null) collections when a file is absent, so rules need no null checks.</summary>
public sealed class CeWorld
{
    /// <summary>The resolved mission directory, or empty when no mission resolves for the active server.</summary>
    public string MissionDir { get; init; } = "";

    /// <summary>True when a mission resolved (CE data is meaningful).</summary>
    public bool HasMission => MissionDir.Length > 0;

    public CeFileSet Types { get; init; } = new(Array.Empty<TypeEntry>());
    public LimitsDef Limits { get; init; } = LimitsDef.Empty;
    public IReadOnlyList<LimitsUserGroup> UserGroups { get; init; } = Array.Empty<LimitsUserGroup>();
    public IReadOnlyList<CeEvent> Events { get; init; } = Array.Empty<CeEvent>();
    public IReadOnlyList<GlobalVar> Globals { get; init; } = Array.Empty<GlobalVar>();
    public IReadOnlyList<SpawnableType> SpawnableTypes { get; init; } = Array.Empty<SpawnableType>();
    public IReadOnlyList<RandomPreset> RandomPresets { get; init; } = Array.Empty<RandomPreset>();
    public IReadOnlyList<SpawnCategory> PlayerSpawns { get; init; } = Array.Empty<SpawnCategory>();
    public IReadOnlyList<CeFileInfo> Files { get; init; } = Array.Empty<CeFileInfo>();

    /// <summary>Type names defined in types.xml (case-insensitive) — the referent set for the
    /// cross-file "does this class exist in CE" checks. Computed once.</summary>
    public IReadOnlySet<string> TypeNames => _typeNames ??=
        Types.Entries.Select(e => e.Name).Where(n => n.Length > 0)
             .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _typeNames;

    /// <summary>The on-disk file name for a kind (for finding display), or the kind name as a fallback.</summary>
    public string FileNameOf(CeKind kind) =>
        Files.FirstOrDefault(f => f.Kind == kind)?.FileName is { Length: > 0 } n ? n : kind.ToString();
}
