using Dzl.Core.Servers;
using FluentAssertions;
public class ServerInstancesTests
{
    [Fact]
    public void Discover_lists_instances_with_serverdz_cfg()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var srvA = Path.Combine(root, "servers", "alpha"); Directory.CreateDirectory(srvA);
        File.WriteAllText(Path.Combine(srvA, "serverDZ.cfg"), "port = 2302;");
        Directory.CreateDirectory(Path.Combine(root, "servers", "nope"));

        var found = ServerInstances.Discover(root).Select(i => i.Name).ToList();
        found.Should().Contain("alpha");
        found.Should().NotContain("nope");
    }

    [Fact]
    public void Discover_missing_root_is_empty()
        => ServerInstances.Discover(@"X:\nope").Should().BeEmpty();

    [Theory]
    [InlineData(new int[0], 2302)]
    [InlineData(new[] { 2302 }, 2303)]
    [InlineData(new[] { 2302, 2303, 2305 }, 2304)]
    public void NextPort_picks_first_free_from_2302(int[] used, int expected)
        => ServerInstances.NextPort(used).Should().Be(expected);
}
