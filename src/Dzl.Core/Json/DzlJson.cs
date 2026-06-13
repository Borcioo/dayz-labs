using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dzl.Core.Json;

/// <summary>The one home for dzl's JSON conventions: snake_case property names and enums serialized as
/// snake_case strings (e.g. <c>FindingSeverity.Error</c> → <c>"error"</c>, <c>ToolKind.LaunchOnly</c> →
/// <c>"launch_only"</c>).</summary>
/// <remarks><see cref="JsonStringEnumConverter"/> still <b>reads</b> bare ints, so files written before
/// the converter existed (build cache, proc state, …) deserialize unchanged.</remarks>
public static class DzlJson
{
    /// <summary>Compact (not indented) — wire/IPC payloads where one value = one line.</summary>
    public static readonly JsonSerializerOptions Snake = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Indented — files on disk and human/MCP-facing output.</summary>
    public static readonly JsonSerializerOptions SnakeIndented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Serialize with <see cref="SnakeIndented"/> — the default for anything a human or MCP client reads.</summary>
    public static string Serialize(object o) => JsonSerializer.Serialize(o, SnakeIndented);
}
