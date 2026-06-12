using System.Text.Json;
using Dzl.Core.Build.Preflight;
using Dzl.Core.Config;
using Dzl.Core.Economy;
using Dzl.Core.Ipc;
using Dzl.Core.Json;
using Dzl.Core.Tools;
using FluentAssertions;
using Xunit;

public class DzlJsonTests
{
    private sealed record Carrier(FindingSeverity Severity, ToolKind Kind);

    [Fact]
    public void Enums_serialize_as_snake_case_strings()
    {
        var json = JsonSerializer.Serialize(new Carrier(FindingSeverity.Error, ToolKind.LaunchOnly), DzlJson.Snake);
        json.Should().Contain("\"severity\":\"error\"");        // single-word name → plain lowercase
        json.Should().Contain("\"kind\":\"launch_only\"");      // multi-word name → snake_case
        JsonSerializer.Serialize(CeOrigin.Vanilla, DzlJson.Snake).Should().Be("\"vanilla\"");
    }

    [Fact]
    public void Enums_still_read_legacy_ints_and_round_trip_strings()
    {
        // files written before the string-enum converter stored bare ints — they must keep loading
        var legacy = JsonSerializer.Deserialize<Carrier>("""{"severity":2,"kind":1}""", DzlJson.Snake)!;
        legacy.Should().Be(new Carrier(FindingSeverity.Error, ToolKind.CliWrappable));

        var roundTripped = JsonSerializer.Deserialize<Carrier>(
            JsonSerializer.Serialize(new Carrier(FindingSeverity.Warning, ToolKind.LaunchOnly), DzlJson.SnakeIndented),
            DzlJson.SnakeIndented);
        roundTripped.Should().Be(new Carrier(FindingSeverity.Warning, ToolKind.LaunchOnly));
    }

    [Fact]
    public void Snake_is_compact_and_snake_indented_is_not()
    {
        JsonSerializer.Serialize(new Carrier(FindingSeverity.Info, ToolKind.LaunchOnly), DzlJson.Snake)
            .Should().NotContain("\n", "the IPC pipe protocol is line-framed — one document per line");
        DzlJson.Serialize(new Carrier(FindingSeverity.Info, ToolKind.LaunchOnly)).Should().Contain("\n");
    }

    [Fact]
    public void Public_aliases_point_at_the_shared_options()
    {
        ConfigStore.Json.Should().BeSameAs(DzlJson.SnakeIndented);
        IpcContract.Json.Should().BeSameAs(DzlJson.Snake);
    }
}
