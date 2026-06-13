using Dzl.Core.Launch;
using FluentAssertions;
public class TrayLauncherTests
{
    [Fact]
    public void Resolve_finds_tray_exe_next_to_base_dir()
    {
        var baseDir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(baseDir, "Dzl.Tray.exe"), "");
        TrayLauncher.Resolve(baseDir).Should().EndWith("Dzl.Tray.exe");
    }

    [Fact]
    public void Resolve_returns_null_when_absent()
        => TrayLauncher.Resolve(Directory.CreateTempSubdirectory().FullName).Should().BeNull();
}
