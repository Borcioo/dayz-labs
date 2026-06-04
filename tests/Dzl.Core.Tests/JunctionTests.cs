using Xunit;
using Dzl.Core.Projects;
using FluentAssertions;

public class JunctionTests
{
    [Fact]
    public void Missing_link_creates_new()
        => Junction.Decide(exists: false, isLink: false, currentTarget: null, desiredTarget: @"D:\P\Mod")
            .Should().Be(LinkAction.CreateNew);

    [Fact]
    public void Real_dir_is_conflict()  // never clobber real content
        => Junction.Decide(exists: true, isLink: false, currentTarget: null, desiredTarget: @"D:\P\Mod")
            .Should().Be(LinkAction.ConflictRealDir);

    [Fact]
    public void Link_to_same_target_is_ok()  // trailing-slash / case insensitive
        => Junction.Decide(exists: true, isLink: true, currentTarget: @"D:\P\Mod", desiredTarget: @"d:\p\mod\")
            .Should().Be(LinkAction.AlreadyOk);

    [Fact]
    public void Link_to_other_target_is_stale()
        => Junction.Decide(exists: true, isLink: true, currentTarget: @"D:\Other", desiredTarget: @"D:\P\Mod")
            .Should().Be(LinkAction.ReplaceStale);

    [Fact]
    public void Dangling_link_is_stale()  // link with no resolvable target
        => Junction.Decide(exists: true, isLink: true, currentTarget: null, desiredTarget: @"D:\P\Mod")
            .Should().Be(LinkAction.ReplaceStale);
}
