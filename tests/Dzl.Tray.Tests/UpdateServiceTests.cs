using Dzl.Tray;
using FluentAssertions;
using Xunit;

namespace Dzl.Tray.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void CanUpdate_is_false_when_not_a_velopack_install()
    {
        // The test host is not a Velopack-installed app, so updates must no-op.
        new UpdateService().CanUpdate.Should().BeFalse();
    }
}
