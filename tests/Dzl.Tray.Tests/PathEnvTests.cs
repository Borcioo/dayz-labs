using Dzl.Tray;
using FluentAssertions;
using Xunit;

namespace Dzl.Tray.Tests;

public class PathEnvTests
{
    [Fact]
    public void EnsurePresent_appends_when_absent()
    {
        PathEnv.EnsurePresent(@"C:\a;C:\b", @"C:\app").Should().Be(@"C:\a;C:\b;C:\app");
    }

    [Fact]
    public void EnsurePresent_is_idempotent_ignoring_case_and_trailing_slash()
    {
        PathEnv.EnsurePresent(@"C:\a;C:\APP\", @"C:\app").Should().Be(@"C:\a;C:\APP\");
    }

    [Fact]
    public void EnsurePresent_handles_empty_or_null()
    {
        PathEnv.EnsurePresent("", @"C:\app").Should().Be(@"C:\app");
        PathEnv.EnsurePresent(null, @"C:\app").Should().Be(@"C:\app");
    }

    [Fact]
    public void Remove_drops_all_matches_ignoring_case_and_trailing_slash()
    {
        PathEnv.Remove(@"C:\a;C:\app;C:\b;C:\APP\", @"C:\app").Should().Be(@"C:\a;C:\b");
    }

    [Fact]
    public void Remove_is_noop_when_absent()
    {
        PathEnv.Remove(@"C:\a;C:\b", @"C:\app").Should().Be(@"C:\a;C:\b");
    }
}
