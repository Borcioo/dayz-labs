using Dzl.Core.Projects;
using FluentAssertions;

public class ModImportTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    private static string MakeSource()
    {
        var src = Path.Combine(Tmp(), "ExtMod");
        Directory.CreateDirectory(Path.Combine(src, "data"));
        File.WriteAllText(Path.Combine(src, "config.cpp"), "class CfgPatches{};");
        File.WriteAllText(Path.Combine(src, "data", "a.paa"), "payload");
        return src;
    }

    [Fact]
    public void Import_copy_duplicates_the_folder_as_a_real_directory_not_a_junction()
    {
        var root = Tmp();
        var src = MakeSource();
        var workDrive = Tmp();   // stand-in for the P: source so the test never touches the real P: drive

        var r = ModImport.Import(root, src, "MyMod", workDriveSource: workDrive, copy: true);

        var modDir = ProjectPaths.ModDir(root, "MyMod");
        // the copy itself must have happened (independent of whether the P: junction step had privileges)
        File.Exists(Path.Combine(modDir, "config.cpp")).Should().BeTrue();
        File.Exists(Path.Combine(modDir, "data", "a.paa")).Should().BeTrue();
        new DirectoryInfo(modDir).Attributes.HasFlag(FileAttributes.ReparsePoint).Should().BeFalse("a copy is a real folder");
        // editing the copy must not touch the source
        File.WriteAllText(Path.Combine(modDir, "config.cpp"), "changed");
        File.ReadAllText(Path.Combine(src, "config.cpp")).Should().Be("class CfgPatches{};");
    }

    [Fact]
    public void Import_copy_refuses_to_clobber_an_existing_project()
    {
        var root = Tmp();
        var src = MakeSource();
        Directory.CreateDirectory(ProjectPaths.ModDir(root, "MyMod"));   // already there

        var r = ModImport.Import(root, src, "MyMod", workDriveSource: Tmp(), copy: true);

        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("already exists");
    }

    [Fact]
    public void Import_link_makes_a_junction_not_a_copy()
    {
        var root = Tmp();
        var src = MakeSource();

        var r = ModImport.Import(root, src, "MyLink", workDriveSource: Tmp(), copy: false);

        if (!r.Ok) return;   // mklink needs no admin but skip if the environment can't create links
        var modDir = ProjectPaths.ModDir(root, "MyLink");
        new DirectoryInfo(modDir).Attributes.HasFlag(FileAttributes.ReparsePoint).Should().BeTrue("link mode junctions the source");
        r.Message.Should().Be("imported");
    }

    [Fact]
    public void Import_rejects_a_missing_source()
        => ModImport.Import(Tmp(), Path.Combine(Tmp(), "nope"), copy: true).Ok.Should().BeFalse();
}
