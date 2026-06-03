using Dzl.Core.Ipc;
using FluentAssertions;
using Xunit;

public class ControlPlaneTests
{
    // Use a unique pipe name so the test forces the direct path even if a real tray
    // (with the automation server) happens to be running on the default pipe.
    private static string NoPipe() => "dzl-test-" + Guid.NewGuid().ToString("N");

    // No tray/pipe server -> ControlPlane must fall back to direct LauncherService.
    [Fact]
    public void Falls_back_to_direct_when_no_server()
    {
        var cfg = Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");
        var cp = new ControlPlane(cfg, NoPipe());
        var statusJson = cp.StatusJson();
        statusJson.Should().Contain("active_preset").And.Contain("default");
    }

    [Fact]
    public void Set_preset_unknown_falls_back_and_reports()
    {
        var cfg = Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");
        var cp = new ControlPlane(cfg, NoPipe());
        cp.SetPresetJson("ghost").Should().Contain("no preset");
    }
}
