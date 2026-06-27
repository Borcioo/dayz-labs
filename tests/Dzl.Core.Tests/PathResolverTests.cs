using Dzl.Core.Build.Preflight;
using FluentAssertions;

public class PathResolverTests
{
    [Fact]
    public void Resolve_strips_a_multi_segment_prefix_to_the_mod_dir()
    {
        var modDir = Directory.CreateTempSubdirectory().FullName;
        var folder = Path.Combine(modDir, "scripts", "4_World");
        Directory.CreateDirectory(folder);

        // $PBOPREFIX$ = "DemoPack\Core" → a files[] entry of the full runtime path must map back to the mod dir.
        var (path, found) = PathResolver.Resolve(@"DemoPack\Core\scripts\4_World", modDir,
            prefix: @"DemoPack\Core", workDriveRoot: null);

        found.Should().BeTrue();
        path.Should().Be(Path.GetFullPath(folder));
    }

    [Fact]
    public void Resolve_still_strips_a_single_segment_prefix()
    {
        var modDir = Directory.CreateTempSubdirectory().FullName;
        var folder = Path.Combine(modDir, "scripts", "4_World");
        Directory.CreateDirectory(folder);

        var (_, found) = PathResolver.Resolve(@"MyMod\scripts\4_World", modDir,
            prefix: "MyMod", workDriveRoot: null);

        found.Should().BeTrue();
    }
}
