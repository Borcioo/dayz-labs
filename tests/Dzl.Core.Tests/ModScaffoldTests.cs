using Dzl.Core.Projects;
using FluentAssertions;

public class ModScaffoldTests
{
    [Fact]
    public void Config_cpp_text_registers_cfgpatches_and_modules()
    {
        var cpp = ModScaffold.ConfigCpp("CoolMod", "Macie");
        cpp.Should().Contain("class CfgPatches");
        cpp.Should().Contain("class CoolMod");
        cpp.Should().Contain("class CfgMods");
        cpp.Should().Contain("Macie");
        cpp.Should().Contain("3_Game").And.Contain("4_World").And.Contain("5_Mission");
    }

    [Fact]
    public void Scaffold_writes_skeleton_and_is_idempotent()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var r1 = ModScaffold.Scaffold(root, "CoolMod", "Macie");
        r1.Ok.Should().BeTrue();
        var modDir = ProjectPaths.ModDir(root, "CoolMod");   // <root>\mods\CoolMod
        r1.ModDir.Should().Be(modDir);
        File.Exists(Path.Combine(modDir, "config.cpp")).Should().BeTrue();
        File.ReadAllText(Path.Combine(modDir, "$PBOPREFIX$")).Trim().Should().Be("CoolMod");
        Directory.Exists(Path.Combine(modDir, "scripts", "3_Game")).Should().BeTrue();
        Directory.Exists(Path.Combine(modDir, "data")).Should().BeTrue();
        File.Exists(Path.Combine(modDir, "README.md")).Should().BeTrue();
        File.Exists(Path.Combine(modDir, ".dzl", "mod.json")).Should().BeTrue();   // metadata folder

        File.WriteAllText(Path.Combine(modDir, "config.cpp"), "EDITED");
        var r2 = ModScaffold.Scaffold(root, "CoolMod", "Macie");
        r2.Ok.Should().BeTrue();
        File.ReadAllText(Path.Combine(modDir, "config.cpp")).Should().Be("EDITED");
    }

    [Fact]
    public void Scaffold_rejects_invalid_name()
        => ModScaffold.Scaffold(Directory.CreateTempSubdirectory().FullName, "1bad", "Macie")
            .Ok.Should().BeFalse();

    [Fact]
    public void Author_cache_round_trips()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        ModScaffold.SaveAuthor(dir, "Macie");
        ModScaffold.CachedAuthor(dir).Should().Be("Macie");
    }
}
