using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dzl.Core.Config;

/// <summary>Machine-global, view-only tray state (e.g. which pack groups are collapsed on My Mods). Kept in a
/// small <c>ui-state.json</c> next to the config — SEPARATE from config/presets, because it's UI state, not a
/// setting (it must not vary per preset or get carried into preset snapshots). Never throws.</summary>
public sealed class UiState
{
    /// <summary>Names of pack groups the user has collapsed on My Mods (case-insensitive).</summary>
    public HashSet<string> CollapsedPacks { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPackCollapsed(string name) => CollapsedPacks.Contains(name);

    public void SetPackCollapsed(string name, bool collapsed)
    {
        if (collapsed) CollapsedPacks.Add(name);
        else CollapsedPacks.Remove(name);
    }

    /// <summary>The ui-state.json path in the same folder as <paramref name="configPath"/>.</summary>
    public static string PathFor(string configPath) =>
        Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "ui-state.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private sealed record Dto(List<string> CollapsedPacks);

    public static UiState Load(string configPath)
    {
        try
        {
            var path = PathFor(configPath);
            if (File.Exists(path))
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json);
                if (dto?.CollapsedPacks is { } packs)
                    return new UiState { CollapsedPacks = new HashSet<string>(packs, StringComparer.OrdinalIgnoreCase) };
            }
        }
        catch { /* missing/corrupt → fresh state */ }
        return new UiState();
    }

    public void Save(string configPath)
    {
        try
        {
            var path = PathFor(configPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new Dto(CollapsedPacks.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()), Json));
        }
        catch { /* best-effort — UI state is not critical */ }
    }
}
