using Dzl.Core.Economy;

namespace Dzl.Core.App;

/// <summary>Facade for editing the active mission's <c>cfgweather.xml</c> — weather channel knobs + toggles.</summary>
public sealed class WeatherService : CeFileService
{
    public WeatherService(string configPath) : base(configPath) { }

    protected override string RelativePath => "cfgweather.xml";
    protected override string? SeedRootName => "weather";

    public string? WeatherPath() => FilePath();

    /// <summary>Parse the config (empty default when the file is absent/unresolvable).</summary>
    public WeatherConfig Load()
    {
        var raw = ReadRaw();
        return raw is null ? new WeatherConfig(false, true, System.Array.Empty<WeatherKnob>()) : WeatherXml.Parse(raw);
    }

    public (bool ok, string msg) SetToggle(string name, bool value) =>
        Edit(doc => WeatherXml.SetToggle(doc, name, value), $"set {name} = {(value ? 1 : 0)}", $"could not set {name}");

    public (bool ok, string msg) SetKnob(string channel, string element, string attr, double value) =>
        Edit(doc => WeatherXml.SetKnob(doc, channel, element, attr, value),
            $"set {channel}/{(element.Length == 0 ? attr : element + "/" + attr)}",
            $"channel '{channel}' not found");
}
