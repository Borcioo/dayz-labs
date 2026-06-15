using Dzl.Core.Economy;
using FluentAssertions;

/// <summary>Pure parse + in-place edit of cfgweather.xml (toggles + channel knobs).</summary>
public class WeatherXmlTests
{
    private const string Xml = """
        <weather reset="0" enable="1">
          <overcast>
            <current actual="0.45" time="120" duration="240"/>
            <limits min="0.0" max="1.0"/>
          </overcast>
          <storm density="1.0" threshold="0.9" timeout="45"/>
        </weather>
        """;

    [Fact]
    public void Parse_reads_toggles_and_knobs_including_channel_self_attrs()
    {
        var c = WeatherXml.Parse(Xml);
        c.Reset.Should().BeFalse();
        c.Enable.Should().BeTrue();
        c.Knobs.Should().Contain(k => k.Channel == "overcast" && k.Element == "current" && k.Attr == "actual" && k.Value == 0.45);
        c.Knobs.Should().Contain(k => k.Channel == "overcast" && k.Element == "limits" && k.Attr == "max" && k.Value == 1.0);
        // storm's attributes sit on the channel element itself (Element == "")
        c.Knobs.Should().Contain(k => k.Channel == "storm" && k.Element == "" && k.Attr == "threshold" && k.Value == 0.9);
    }

    [Fact]
    public void SetKnob_updates_a_subelement_and_a_self_attr()
    {
        var doc = WeatherXml.ParseDoc(Xml);
        WeatherXml.SetKnob(doc, "overcast", "current", "actual", 0.8).Should().BeTrue();
        WeatherXml.SetKnob(doc, "storm", "", "timeout", 30).Should().BeTrue();
        var c = WeatherXml.Parse(WeatherXml.ToXml(doc));
        c.Knobs.Single(k => k.Channel == "overcast" && k.Element == "current" && k.Attr == "actual").Value.Should().Be(0.8);
        c.Knobs.Single(k => k.Channel == "storm" && k.Attr == "timeout").Value.Should().Be(30);
    }

    [Fact]
    public void SetToggle_flips_enable()
    {
        var doc = WeatherXml.ParseDoc(Xml);
        WeatherXml.SetToggle(doc, "enable", false).Should().BeTrue();
        WeatherXml.Parse(WeatherXml.ToXml(doc)).Enable.Should().BeFalse();
    }

    [Fact]
    public void Parse_is_safe_on_malformed()
    {
        WeatherXml.Parse("garbage").Knobs.Should().BeEmpty();
    }
}
