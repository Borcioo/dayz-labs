using Dzl.Core.Config;
using Dzl.Core.Ipc;
using FluentAssertions;
using Xunit;

public class ControlPlaneTests
{
    // Use a unique pipe name so the test forces the direct path even if a real tray
    // (with the automation server) happens to be running on the default pipe.
    private static string NoPipe() => "dzl-test-" + Guid.NewGuid().ToString("N");

    // config.json with a temp ProjectsRoot so instances live under a temp dir (never the real profile).
    private static string TmpConfig()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var p = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(dir, "projects") }, p);
        return p;
    }

    // No tray/pipe server -> ControlPlane must fall back to direct LauncherService.
    [Fact]
    public void Falls_back_to_direct_when_no_server()
    {
        var cp = new ControlPlane(TmpConfig(), NoPipe());
        var statusJson = cp.StatusJson();
        statusJson.Should().Contain("active_preset").And.Contain("default");
    }

    [Fact]
    public void Set_preset_unknown_falls_back_and_reports()
    {
        var cp = new ControlPlane(TmpConfig(), NoPipe());
        cp.SetPresetJson("ghost").Should().Contain("no preset");
    }
}
