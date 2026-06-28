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

    [Fact]
    public void Discover_treats_a_folder_of_child_mods_as_a_pack()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var mods = ProjectPaths.ModsDir(root);
        // standalone
        var solo = Path.Combine(mods, "Alpha"); Directory.CreateDirectory(solo);
        File.WriteAllText(Path.Combine(solo, "config.cpp"), "class CfgPatches{};");
        // a pack: no project marker at its own level, two child mods inside
        var pack = Path.Combine(mods, "paczka");
        var scripts = Path.Combine(pack, "scripts"); Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "config.cpp"), "class CfgPatches{};");
        var ce = Path.Combine(pack, "ce"); Directory.CreateDirectory(ce);
        File.WriteAllText(Path.Combine(ce, "$PBOPREFIX$"), "ce");
        // a child with no marker is not counted
        Directory.CreateDirectory(Path.Combine(pack, "docs"));

        var found = ModProjects.Discover(root);

        var alpha = found.Single(p => p.Name == "Alpha");
        alpha.IsPack.Should().BeFalse();
        alpha.Children.Should().BeEmpty();

        var p = found.Single(x => x.Name == "paczka");
        p.IsPack.Should().BeTrue();
        p.Children.Select(c => c.Name).Should().BeEquivalentTo("scripts", "ce");
        p.Children.Should().OnlyContain(c => !c.IsPack);
    }

    [Fact]
    public void Discover_prefers_standalone_when_a_folder_is_itself_a_project_and_has_child_projects()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var mods = ProjectPaths.ModsDir(root);
        var dir = Path.Combine(mods, "Hybrid"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.cpp"), "class CfgPatches{};");   // own config
        var inner = Path.Combine(dir, "sub"); Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "config.cpp"), "class CfgPatches{};");

        var h = ModProjects.Discover(root).Single(p => p.Name == "Hybrid");
        h.IsPack.Should().BeFalse("its own config.cpp makes it a single mod, not a pack");
        h.Children.Should().BeEmpty();
    }

    [Fact]
    public void Discover_skips_a_folder_with_no_projects_at_any_level()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var mods = ProjectPaths.ModsDir(root);
        var junk = Path.Combine(mods, "junk", "deeper"); Directory.CreateDirectory(junk);

        ModProjects.Discover(root).Should().NotContain(p => p.Name == "junk");
    }

    [Fact]
    public void Discover_flags_a_link_imported_mod_as_IsImportLink()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var mods = ProjectPaths.ModsDir(root);
        Directory.CreateDirectory(mods);
        // a real (created/copied) mod
        var real = Path.Combine(mods, "Real"); Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "config.cpp"), "class CfgPatches{};");
        // an external source linked into mods\ (import-as-link)
        var ext = Path.Combine(root, "external"); Directory.CreateDirectory(ext);
        File.WriteAllText(Path.Combine(ext, "config.cpp"), "class CfgPatches{};");
        if (!Junction.Ensure(Path.Combine(mods, "Linked"), ext).Ok) return;   // can't link here → skip

        var found = ModProjects.Discover(root);

        found.Single(p => p.Name == "Real").IsImportLink.Should().BeFalse();
        found.Single(p => p.Name == "Linked").IsImportLink.Should().BeTrue();
    }

    [Fact]
    public void Discover_surfaces_a_dead_junction_as_broken_without_killing_the_list()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var mods = ProjectPaths.ModsDir(root);
        Directory.CreateDirectory(mods);
        // a healthy mod that must still appear
        var good = Path.Combine(mods, "Good"); Directory.CreateDirectory(good);
        File.WriteAllText(Path.Combine(good, "config.cpp"), "class CfgPatches{};");

        // a mod folder that's a junction whose target gets moved/deleted out from under us
        var target = Path.Combine(root, "ghost_target");
        var ens = Junction.Ensure(Path.Combine(mods, "Ghost"), target);
        if (!ens.Ok) return;                       // can't make a junction here (no privilege) → skip
        Directory.Delete(target, recursive: true); // now mods\Ghost dangles

        var found = ModProjects.Discover(root);     // must NOT throw on the dead junction

        found.Single(p => p.Name == "Good").IsBroken.Should().BeFalse();
        var ghost = found.Single(p => p.Name == "Ghost");   // surfaced, not hidden
        ghost.IsBroken.Should().BeTrue();
        ghost.IsImportLink.Should().BeTrue();
    }

    [Fact]
    public void Junction_Remove_clears_a_dangling_junction()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        if (!Junction.Ensure(link, target).Ok) return;
        Directory.Delete(target, recursive: true);          // link now dangles
        Junction.IsReparsePointEntry(link).Should().BeTrue();

        Junction.Remove(link);                              // must clear the dead link

        Junction.IsReparsePointEntry(link).Should().BeFalse();
        Directory.Exists(target).Should().BeFalse();        // (it was already gone; sanity)
    }
}
