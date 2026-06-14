using System.Windows.Threading;
using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="ChanceField"/>'s inline auto-commit. Regression guard for the
/// "preset rename / select fires a wave of phantom item updates" bug: a TwoWay binding setting Value while
/// the grid (re)builds rows must NOT arm the debounce-commit timer — only a real edit (focus within) may.</summary>
public class ChanceFieldTests
{
    [WpfFact]
    public void Programmatic_value_change_without_focus_does_not_auto_commit()
    {
        var cf = new ChanceField { Inline = true };
        var commits = 0;
        cf.ValueCommitted += (_, _) => commits++;

        cf.Value = 0.22;   // simulates the row's {Binding Chance} push when the items grid rebuilds

        PumpFor(TimeSpan.FromMilliseconds(550));   // well past the 350ms inline debounce window

        commits.Should().Be(0,
            "a value set by binding (no user focus) must not fire a phantom ValueCommitted / item save");
    }

    /// <summary>Run the dispatcher message loop for <paramref name="d"/> so any queued DispatcherTimer
    /// (the inline commit timer) gets a chance to tick.</summary>
    private static void PumpFor(TimeSpan d)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = d };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
