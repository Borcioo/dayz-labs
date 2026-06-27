using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Projects;
using FluentAssertions;

namespace Dzl.Core.Tests;

// Early-return paths of BuildService.BuildPack that need no DayZ Tools / P: drive (they fail before
// any AddonBuilder/work-drive step). The full pack build is verified live (needs the toolchain).
public class PackBuildTests
{
    private static (string configPath, string root) Setup()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");
        Profiles.EnsureDefault(configPath);
        var (cfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(cfg with { ProjectsRoot = root, DayzPath = tmp }, configPath);
        return (configPath, root);
    }

    [Fact]
    public void BuildPack_rejects_an_invalid_name()
    {
        var (configPath, _) = Setup();
        new BuildService(configPath).BuildPack("bad name").Ok.Should().BeFalse();
    }

    [Fact]
    public void BuildPack_fails_when_the_folder_is_not_a_pack()
    {
        var (configPath, root) = Setup();
        // a single mod (own config) is not a pack
        var dir = ProjectPaths.ModDir(root, "Solo"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.cpp"), "class CfgPatches{};");

        var r = new BuildService(configPath).BuildPack("Solo");
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("pack");
    }

    [Fact]
    public void BuildPack_fails_when_no_child_mods_are_selected()
    {
        var (configPath, root) = Setup();
        var pack = ProjectPaths.ModDir(root, "paczka");
        var s = Path.Combine(pack, "scripts"); Directory.CreateDirectory(s);
        File.WriteAllText(Path.Combine(s, "config.cpp"), "class CfgPatches{};");

        // selecting a child that doesn't exist → nothing to build
        var r = new BuildService(configPath).BuildPack("paczka", new[] { "doesNotExist" });
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("no");
    }
}
