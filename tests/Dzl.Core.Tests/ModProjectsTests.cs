using Xunit;
using Dzl.Core.Projects;
using FluentAssertions;

public class ModProjectsTests
{
    [Fact]
    public void Discover_lists_subdirs_that_look_like_mod_projects()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var mods = ProjectPaths.ModsDir(root);
        var a = Path.Combine(mods, "Alpha"); Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "$PBOPREFIX$"), "Alpha");
        var b = Path.Combine(mods, "Beta"); Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(b, "config.cpp"), "class CfgPatches{};");
        Directory.CreateDirectory(Path.Combine(mods, "NotAMod"));

        var found = ModProjects.Discover(root).Select(p => p.Name).ToList();
        found.Should().Contain("Alpha").And.Contain("Beta");
        found.Should().NotContain("NotAMod");
    }

    [Fact]
    public void Discover_on_missing_root_is_empty()
        => ModProjects.Discover(@"X:\nope").Should().BeEmpty();
}
