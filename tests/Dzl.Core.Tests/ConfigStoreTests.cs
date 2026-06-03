using Dzl.Core.Config;
using FluentAssertions;
using Xunit;

public class ConfigStoreTests
{
    [Fact]
    public void Default_has_expected_scalars()
    {
        var c = DzlConfig.Default();
        c.Port.Should().Be(2302);
        c.Mode.Should().Be("debug");
        c.ConnectIp.Should().Be("127.0.0.1");
        c.ActivePreset.Should().Be("");
        c.ServerParamsDebug.Should().Contain("-filePatching");
        c.ClientParamsDebug.Should().Contain("-window");
    }
}
