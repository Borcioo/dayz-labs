using Dzl.Core.Projects;
using FluentAssertions;

public class FileOpsTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void ForceDeleteDirectory_removes_a_normal_tree_including_readonly_files()
    {
        var dir = Tmp();
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        var ro = Path.Combine(dir, "sub", "ro.txt");
        File.WriteAllText(ro, "x");
        File.SetAttributes(ro, FileAttributes.ReadOnly);   // like git pack files

        FileOps.ForceDeleteDirectory(dir);

        Directory.Exists(dir).Should().BeFalse();
    }

    [Fact]
    public void ForceDeleteDirectory_on_a_junction_removes_the_link_but_NEVER_the_target()
    {
        var root = Tmp();
        // the external "source" we must never lose
        var target = Path.Combine(root, "source");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "precious.txt"), "do not delete");

        // a junction pointing at it (e.g. an imported-as-link mod)
        var link = Path.Combine(root, "link");
        if (!Junction.Ensure(link, target).Ok) return;   // can't make a link here → skip

        FileOps.ForceDeleteDirectory(link);

        Directory.Exists(link).Should().BeFalse("the junction itself is removed");
        File.Exists(Path.Combine(target, "precious.txt")).Should().BeTrue("the link's target must survive");
    }

    [Fact]
    public void ForceDeleteDirectory_does_not_descend_into_a_nested_junction_target()
    {
        var root = Tmp();
        var target = Path.Combine(root, "source");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "precious.txt"), "keep");

        var parent = Path.Combine(root, "parent");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "own.txt"), "y");
        if (!Junction.Ensure(Path.Combine(parent, "nested"), target).Ok) return;

        FileOps.ForceDeleteDirectory(parent);

        Directory.Exists(parent).Should().BeFalse();
        File.Exists(Path.Combine(target, "precious.txt")).Should().BeTrue("a nested junction's target must survive");
    }
}
