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

    [Fact]
    public void EnsurePresent_does_not_double_the_separator_when_current_ends_in_semicolon()
    {
        PathEnv.EnsurePresent(@"C:\a;", @"C:\app").Should().Be(@"C:\a;C:\app");
    }

    [Fact]
    public void EnsurePresent_and_Remove_tolerate_whitespace_padded_entries()
    {
        PathEnv.EnsurePresent(@"C:\a; C:\app ", @"C:\app").Should().Be(@"C:\a; C:\app ");
        PathEnv.Remove(@"C:\a; C:\app ;C:\b", @"C:\app").Should().Be(@"C:\a;C:\b");
    }
}
