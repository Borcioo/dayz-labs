using Dzl.Core.Economy;
using FluentAssertions;

public class CeFileSetTests
{
    [Fact]
    public void Group_by_source_file_returns_entries_per_file()
    {
        var a = new TypeEntry { Name = "Apple", SourceFile = @"C:\m\db\types.xml" };
        var b = new TypeEntry { Name = "AK",    SourceFile = @"C:\m\ce\Mod\t.xml" };
        var set = new CeFileSet(new[] { a, b });
        var groups = set.BySourceFile();
        groups.Should().ContainKey(@"C:\m\db\types.xml");
        groups[@"C:\m\db\types.xml"].Should().ContainSingle(e => e.Name == "Apple");
    }

    [Fact]
    public void DistinctSources_lists_each_source_file_once()
    {
        var set = new CeFileSet(new[]
        {
            new TypeEntry { Name = "A", SourceFile = "f1" },
            new TypeEntry { Name = "B", SourceFile = "f1" },
            new TypeEntry { Name = "C", SourceFile = "f2" },
        });
        set.DistinctSources().Should().BeEquivalentTo("f1", "f2");
    }
}
