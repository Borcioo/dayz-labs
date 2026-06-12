using Dzl.Core.Procs;
using FluentAssertions;
using Xunit;

namespace Dzl.Core.Tests;

// Process tests (Windows cmd.exe) — fast, no external tools needed.
public class ProcRunnerTests
{
    [Fact]
    public void Captures_stdout_and_exit_code()
    {
        var r = ProcRunner.Run("cmd.exe", new[] { "/c", "echo hello" });
        r.Ok.Should().BeTrue();
        r.Code.Should().Be(0);
        r.StdOut.Should().Be("hello");
        r.TimedOut.Should().BeFalse();
    }

    [Fact]
    public void Captures_stderr_separately()
    {
        var r = ProcRunner.Run("cmd.exe", new[] { "/c", "echo oops 1>&2" });
        r.Ok.Should().BeTrue();
        r.StdOut.Should().BeEmpty();
        r.StdErr.Should().Be("oops");
        r.AllOutput.Should().Be("oops");
    }

    [Fact]
    public void Nonzero_exit_is_not_ok()
    {
        var r = ProcRunner.Run("cmd.exe", new[] { "/c", "exit 3" });
        r.Ok.Should().BeFalse();
        r.Code.Should().Be(3);
    }

    [Fact]
    public void Launch_failure_never_throws()
    {
        var r = ProcRunner.Run(@"C:\definitely\not\a\real\dzl-test.exe", Array.Empty<string>());
        r.Ok.Should().BeFalse();
        r.Code.Should().Be(-1);
        r.StdErr.Should().NotBeEmpty();
    }

    [Fact]
    public void Timeout_kills_the_process_and_reports()
    {
        var r = ProcRunner.Run("cmd.exe", new[] { "/c", "ping -n 6 127.0.0.1 >nul" },
            new RunOpts(TimeoutMs: 500));
        r.TimedOut.Should().BeTrue();
        r.Ok.Should().BeFalse();
        r.Code.Should().Be(-1);
        r.StdErr.Should().Contain("timed out");
    }

    [Fact]
    public void Env_overrides_reach_the_child()
    {
        var r = ProcRunner.Run("cmd.exe", new[] { "/c", "echo %DZL_PROC_TEST%" },
            new RunOpts(Env: new Dictionary<string, string> { ["DZL_PROC_TEST"] = "ping-pong" }));
        r.StdOut.Should().Be("ping-pong");
    }

    [Fact]
    public void OnLine_streams_each_line()
    {
        var lines = new List<string>();
        var r = ProcRunner.Run("cmd.exe", new[] { "/c", "echo one&& echo two" },
            new RunOpts(OnLine: lines.Add));
        r.Ok.Should().BeTrue();
        lines.Should().Equal("one", "two");
    }
}
