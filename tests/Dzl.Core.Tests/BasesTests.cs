using FluentAssertions;
using Xunit;
using BaseMgr = Dzl.Core.Bases.ServerBases;

public class BasesTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void CreateEmpty_writes_descriptor_and_lists()
    {
        var root = Tmp();
        BaseMgr.CreateEmpty(root, "vanilla").ok.Should().BeTrue();
        File.Exists(BaseMgr.BaseFile(root, "vanilla")).Should().BeTrue();
        var list = BaseMgr.List(root);
        list.Should().ContainSingle(b => b.Name == "vanilla" && b.Source == "custom");
    }

    [Fact]
    public void CreateFromInstall_copies_mission_without_storage_and_tags_source()
    {
        var root = Tmp();
        var install = Tmp();
        var mission = Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus");
        Directory.CreateDirectory(mission);
        File.WriteAllText(Path.Combine(mission, "init.c"), "");
        Directory.CreateDirectory(Path.Combine(mission, "storage_1"));   // live persistence — must NOT copy

        BaseMgr.CreateFromInstall(root, "chern", install, "dayzOffline.chernarusplus").ok.Should().BeTrue();

        var bMission = Path.Combine(BaseMgr.BaseDir(root, "chern"), "mpmissions", "dayzOffline.chernarusplus");
        File.Exists(Path.Combine(bMission, "init.c")).Should().BeTrue();
        Directory.Exists(Path.Combine(bMission, "storage_1")).Should().BeFalse();
        File.Exists(Path.Combine(BaseMgr.BaseDir(root, "chern"), "serverDZ.cfg")).Should().BeTrue();
        BaseMgr.List(root).Should().ContainSingle(b => b.Name == "chern" && b.Source == "dayz-install" && b.Mission == "dayzOffline.chernarusplus");
    }

    [Fact]
    public void CopyInto_brings_cfg_and_mission_into_an_instance()
    {
        var root = Tmp();
        var install = Tmp();
        var mission = Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus");
        Directory.CreateDirectory(mission);
        File.WriteAllText(Path.Combine(mission, "init.c"), "");
        BaseMgr.CreateFromInstall(root, "chern", install, "dayzOffline.chernarusplus");

        var instance = Path.Combine(Tmp(), "servers", "alpha");
        BaseMgr.CopyInto(root, "chern", instance);

        File.Exists(Path.Combine(instance, "serverDZ.cfg")).Should().BeTrue();
        File.Exists(Path.Combine(instance, "mpmissions", "dayzOffline.chernarusplus", "init.c")).Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_duplicate_and_invalid_names()
    {
        var root = Tmp();
        BaseMgr.CreateEmpty(root, "x").ok.Should().BeTrue();
        BaseMgr.CreateEmpty(root, "x").ok.Should().BeFalse();      // duplicate
        BaseMgr.CreateEmpty(root, "1bad").ok.Should().BeFalse();   // invalid name
    }

    [Fact]
    public void Delete_removes_the_base()
    {
        var root = Tmp();
        BaseMgr.CreateEmpty(root, "x");
        BaseMgr.Delete(root, "x").Should().BeTrue();
        BaseMgr.List(root).Should().BeEmpty();
        BaseMgr.Delete(root, "x").Should().BeFalse();
    }
}
