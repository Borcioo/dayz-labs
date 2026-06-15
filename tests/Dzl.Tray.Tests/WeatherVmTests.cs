using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>Tests for <see cref="WeatherVm"/> (World "Weather" tab = cfgweather.xml): channel knobs grouped
/// into cards, toggle + knob persistence, numeric validation.</summary>
public class WeatherVmTests
{
    private const string Fixture = """
        <weather reset="0" enable="1">
          <overcast>
            <current actual="0.45" time="120" duration="240"/>
            <limits min="0.0" max="1.0"/>
          </overcast>
          <storm density="1.0" threshold="0.9" timeout="45"/>
        </weather>
        """;

    private static WeatherVm Load(out string cfg)
    {
        cfg = CeScaffold.Mission(("cfgweather.xml", Fixture));
        var vm = new WeatherVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static WeatherVm Reloaded(string cfg)
    {
        var vm = new WeatherVm(cfg, _ => true);
        vm.Reload();
        return vm;
    }

    private static WeatherKnobVm Knob(WeatherVm vm, string channel, string attr) =>
        vm.Channels.Single(c => c.Name == channel).Knobs.First(k => k.Attr == attr);

    [Fact]
    public void Reload_groups_channels_and_reads_toggles()
    {
        var vm = Load(out _);
        vm.EnableFlag.Should().BeTrue();
        vm.Channels.Select(c => c.Name).Should().Contain(new[] { "overcast", "storm" });
    }

    [Fact]
    public void Editing_a_knob_persists()
    {
        var vm = Load(out var cfg);
        var k = Knob(vm, "overcast", "actual");
        k.ValueText = "0.8";
        k.Commit();
        Knob(Reloaded(cfg), "overcast", "actual").ValueText.Should().Be("0.8");
    }

    [Fact]
    public void A_non_numeric_knob_is_rejected()
    {
        var vm = Load(out var cfg);
        var k = Knob(vm, "overcast", "actual");
        k.ValueText = "abc";
        k.Commit();
        vm.Status.Should().StartWith("✗");
        Knob(Reloaded(cfg), "overcast", "actual").ValueText.Should().Be("0.45");
    }

    [Fact]
    public void Toggling_enable_persists()
    {
        var vm = Load(out var cfg);
        vm.EnableFlag = false;
        Reloaded(cfg).EnableFlag.Should().BeFalse();
    }
}
