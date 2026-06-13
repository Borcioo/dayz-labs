using Dzl.Core.App;
using Dzl.Core.Ipc;
using FluentAssertions;

public class IpcDispatcherTests
{
    private static LauncherService Svc() =>
        new(Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json"));

    [Fact]
    public void Status_request_returns_ok_with_json()
    {
        var r = IpcDispatcher.Handle(new IpcRequest("status", null), Svc());
        r.Ok.Should().BeTrue();
        r.Json.Should().Contain("active_preset");
    }

    [Fact]
    public void Set_preset_unknown_returns_ok_false_message()
    {
        var r = IpcDispatcher.Handle(new IpcRequest("set_preset", new() { ["name"] = "ghost" }), Svc());
        // dispatcher succeeds at routing; the op result inside reports failure
        r.Ok.Should().BeTrue();
        r.Json.Should().Contain("no preset");
    }

    [Fact]
    public void Unknown_method_returns_error()
    {
        var r = IpcDispatcher.Handle(new IpcRequest("frobnicate", null), Svc());
        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("unknown method");
    }

    [Fact]
    public void Request_response_round_trip_json()
    {
        var req = new IpcRequest("logs", new() { ["which"] = "script", ["lines"] = "5" });
        var json = System.Text.Json.JsonSerializer.Serialize(req, IpcContract.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<IpcRequest>(json, IpcContract.Json)!;
        back.Method.Should().Be("logs");
        back.Args!["which"].Should().Be("script");
    }
}
