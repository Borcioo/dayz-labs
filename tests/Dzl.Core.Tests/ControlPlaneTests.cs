using Dzl.Core.Ipc;
using FluentAssertions;
using Xunit;

public class ControlPlaneTests
{
    // No tray/pipe server running in the test -> ControlPlane must fall back to direct LauncherService.
    [Fact]
    public void Falls_back_to_direct_when_no_server()
    {
        var cfg = Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");
        var cp = new ControlPlane(cfg);
        var statusJson = cp.StatusJson();
        statusJson.Should().Contain("active_preset").And.Contain("default");
    }

    [Fact]
    public void Set_preset_unknown_falls_back_and_reports()
    {
        var cfg = Path.Combine(Directory.CreateTempSubdirectory().FullName, "config.json");
        var cp = new ControlPlane(cfg);
        cp.SetPresetJson("ghost").Should().Contain("no preset");
    }
}
