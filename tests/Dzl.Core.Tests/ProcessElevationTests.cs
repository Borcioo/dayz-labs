using Dzl.Core.Tools;
using FluentAssertions;

namespace Dzl.Core.Tests;

// BuildCommandLine feeds CreateProcessWithTokenW (the de-elevated WorkDrive path) — quoting must
// follow the MSVCRT argv rules, especially backslashes before a closing quote.
public class ProcessElevationTests
{
    [Fact]
    public void Plain_args_join_unquoted()
    {
        ProcessElevation.BuildCommandLine(@"C:\t\WorkDrive.exe", new[] { "/mount", "P:" })
            .Should().Be("\"C:\\t\\WorkDrive.exe\" /mount P:");
    }

    [Fact]
    public void Arg_with_space_is_quoted()
    {
        ProcessElevation.BuildCommandLine("x.exe", new[] { @"C:\DayZ Projects\src" })
            .Should().Be("\"x.exe\" \"C:\\DayZ Projects\\src\"");
    }

    [Fact]
    public void Trailing_backslash_before_closing_quote_is_doubled()
    {
        // The old Replace-based quoting produced "C:\DayZ Projects\" — the final \" parses as an
        // escaped quote and swallows the next argument.
        ProcessElevation.BuildCommandLine("x.exe", new[] { @"C:\DayZ Projects\", "next" })
            .Should().Be("\"x.exe\" \"C:\\DayZ Projects\\\\\" next");
    }

    [Fact]
    public void Embedded_quote_is_escaped_with_preceding_backslashes_doubled()
    {
        ProcessElevation.BuildCommandLine("x.exe", new[] { "a\\\"b" })
            .Should().Be("\"x.exe\" \"a\\\\\\\"b\"");
    }

    [Fact]
    public void Empty_arg_becomes_empty_quotes()
    {
        ProcessElevation.BuildCommandLine("x.exe", new[] { "" })
            .Should().Be("\"x.exe\" \"\"");
    }
}
