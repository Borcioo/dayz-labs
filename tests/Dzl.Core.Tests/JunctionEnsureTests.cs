using Dzl.Core.Projects;
using FluentAssertions;

public class JunctionEnsureTests
{
    [Fact]
    public void Ensure_refuses_when_a_real_directory_occupies_the_link_path()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var link = Path.Combine(root, "RealDir");
        Directory.CreateDirectory(link);
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);

        var res = Junction.Ensure(link, target);
        res.Action.Should().Be(LinkAction.ConflictRealDir);
        res.Ok.Should().BeFalse();
        Directory.Exists(link).Should().BeTrue();
    }

    [Fact]
    public void Ensure_creates_a_link_when_nothing_is_there()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(root, "Link");

        var res = Junction.Ensure(link, target);
        res.Action.Should().Be(LinkAction.CreateNew);
        res.Ok.Should().BeTrue();
        Directory.Exists(link).Should().BeTrue();
        new DirectoryInfo(link).LinkTarget.Should().NotBeNull();
    }

    [Fact]
    public void Ensure_is_idempotent_for_an_existing_correct_link()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(root, "Link");
        Junction.Ensure(link, target).Ok.Should().BeTrue();

        var res = Junction.Ensure(link, target);
        res.Action.Should().Be(LinkAction.AlreadyOk);
        res.Ok.Should().BeTrue();
    }
}
