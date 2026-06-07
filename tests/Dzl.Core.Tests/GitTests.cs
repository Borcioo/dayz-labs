using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Vcs;
using FluentAssertions;
using Xunit;

public class GitTests
{
    [Theory]
    [InlineData("git@github.com:foreto/dzl.git", "https://github.com/foreto/dzl")]
    [InlineData("https://github.com/foreto/dzl.git", "https://github.com/foreto/dzl")]
    [InlineData("https://github.com/foreto/dzl", "https://github.com/foreto/dzl")]
    [InlineData("ssh://git@github.com/foreto/dzl.git", "https://github.com/foreto/dzl")]
    [InlineData("", null)]
    [InlineData("not-a-remote", null)]
    public void ToBrowserUrl_normalises_remotes(string remote, string? expected)
        => Git.ToBrowserUrl(remote).Should().Be(expected);


    private static (string configPath, string root) TmpConfig()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var root = Path.Combine(dir, "projects");
        var configPath = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = root }, configPath);
        return (configPath, root);
    }

    // --- pure porcelain v2 parsing ---

    [Fact]
    public void ParseStatus_clean_branch_with_upstream_and_ahead_behind()
    {
        var porcelain =
            "# branch.oid 1111111111111111111111111111111111111111\n" +
            "# branch.head main\n" +
            "# branch.upstream origin/main\n" +
            "# branch.ab +2 -1\n";
        var s = Git.ParseStatus(porcelain);
        s.IsRepo.Should().BeTrue();
        s.Branch.Should().Be("main");
        s.Ahead.Should().Be(2);
        s.Behind.Should().Be(1);
        s.HasRemote.Should().BeTrue();
        s.Dirty.Should().BeFalse();
        s.Detail.Should().Be("clean");
    }

    [Fact]
    public void ParseStatus_dirty_when_changed_or_untracked_entries_present()
    {
        var porcelain =
            "# branch.head feature\n" +
            "# branch.ab +0 -0\n" +
            "1 .M N... 100644 100644 100644 aaa bbb config.cpp\n" +
            "? scripts/new.c\n";
        var s = Git.ParseStatus(porcelain);
        s.Branch.Should().Be("feature");
        s.Dirty.Should().BeTrue();
        s.HasRemote.Should().BeFalse();   // no upstream line
        s.Detail.Should().Be("dirty");
    }

    [Fact]
    public void ParseStatus_no_ab_line_leaves_counts_zero()
    {
        var s = Git.ParseStatus("# branch.head main\n");
        s.Ahead.Should().Be(0);
        s.Behind.Should().Be(0);
        s.Dirty.Should().BeFalse();
    }

    // --- RepoService input validation (short-circuits before shelling out) ---

    [Fact]
    public void Status_rejects_invalid_name_without_shelling()
    {
        var (configPath, _) = TmpConfig();
        var s = new RepoService(configPath).Status("1bad");
        s.IsRepo.Should().BeFalse();
        s.Detail.Should().Contain("invalid mod name");
    }

    [Fact]
    public void Publish_fails_when_not_a_mod_project()
    {
        var (configPath, _) = TmpConfig();
        var r = new RepoService(configPath).Publish("Ghost");
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("not a mod project");
    }

    [Fact]
    public void Release_requires_a_tag()
    {
        var (configPath, _) = TmpConfig();
        var r = new RepoService(configPath).Release("AnyMod", "");
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("tag required");
    }
}
