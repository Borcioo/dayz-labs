using Dzl.Tray.ViewModels;
using FluentAssertions;

namespace Dzl.Tray.Tests;

/// <summary>The log pane keeps raw lines and projects a filtered+searched view into <see cref="LogPaneVm.Text"/>.
/// These guard that the quick filter / search box / counts behave (the classification itself is covered by
/// LogLineClassifierTests in Core).</summary>
public class LogPaneVmTests
{
    private static LogPaneVm Seeded()
    {
        var p = new LogPaneVm("rpt", "RPT");
        p.AppendBatch(new[]
        {
            "ENTITY (W): Unknown object class 'pond'",
            "SCRIPT (E): Class 'Foo' not found",
            "Player \"Survivor\" is connected",
            "Mission read.",
            "neutral data line",
        });
        return p;
    }

    [Fact]
    public void Default_filter_shows_all_lines()
    {
        var p = Seeded();
        p.Text.Split('\n').Should().HaveCount(5);
        p.TotalCount.Should().Be(5);
        p.VisibleCount.Should().Be(5);
    }

    [Fact]
    public void Setting_a_filter_projects_only_that_bucket()
    {
        var p = Seeded();
        p.Filter = "errors";
        p.Text.Should().Be("SCRIPT (E): Class 'Foo' not found");
        p.VisibleCount.Should().Be(1);
        p.TotalCount.Should().Be(5);   // total is unfiltered
    }

    [Fact]
    public void Search_narrows_within_the_filter()
    {
        var p = Seeded();
        p.Search = "pond";
        p.Text.Should().Be("ENTITY (W): Unknown object class 'pond'");
        p.VisibleCount.Should().Be(1);
    }

    [Fact]
    public void Clearing_the_filter_back_to_all_restores_every_line()
    {
        var p = Seeded();
        p.Filter = "connections";
        p.VisibleCount.Should().Be(1);
        p.Filter = "all";
        p.VisibleCount.Should().Be(5);
    }

    [Fact]
    public void A_new_batch_respects_the_active_filter()
    {
        var p = Seeded();
        p.Filter = "errors";
        p.AppendBatch(new[] { "another (E): boom", "harmless line" });
        p.VisibleCount.Should().Be(2);          // two error lines now
        p.Text.Should().Contain("another (E): boom").And.NotContain("harmless line");
    }
}
