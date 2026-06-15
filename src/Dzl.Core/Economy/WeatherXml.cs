using System.Xml.Linq;

namespace Dzl.Core.Economy;

/// <summary>One numeric weather knob: which channel (overcast/fog/rain/…), which sub-element (current/limits/…
/// or "" for an attribute on the channel itself, e.g. storm density), the attribute, and its value.</summary>
public sealed record WeatherKnob(string Channel, string Element, string Attr, double Value);

/// <summary>Parsed cfgweather.xml: the two top toggles + all numeric knobs across channels.</summary>
public sealed record WeatherConfig(bool Reset, bool Enable, IReadOnlyList<WeatherKnob> Knobs);

/// <summary>
/// Pure parse + in-place edit of a mission's <c>cfgweather.xml</c> — weather channels (overcast, fog, rain,
/// wind, snowfall, storm) and their numeric knobs. Generic over channel/element/attr so the whole tree is
/// editable without hard-coding ~70 fields. <see cref="Parse"/> never throws; edits preserve comments/order.
/// </summary>
public static class WeatherXml
{
    private static double Dbl(string? raw) => CeNum.Dbl(raw);
    private static string Str(double v) => CeNum.Str(v);

    public static WeatherConfig Parse(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return new WeatherConfig(false, true, System.Array.Empty<WeatherKnob>());

            var reset = CeNum.Bool01(root.Attribute("reset")?.Value ?? "0");
            var enable = root.Attribute("enable") is { } en ? CeNum.Bool01(en.Value) : true;

            var knobs = new List<WeatherKnob>();
            foreach (var ch in root.Elements())
            {
                var cname = ch.Name.LocalName;
                // Attributes on the channel element itself (e.g. <storm density threshold timeout/>).
                foreach (var a in ch.Attributes())
                    knobs.Add(new WeatherKnob(cname, "", a.Name.LocalName, Dbl(a.Value)));
                // Attributes on each sub-element (e.g. overcast/<current actual time duration/>).
                foreach (var el in ch.Elements())
                    foreach (var a in el.Attributes())
                        knobs.Add(new WeatherKnob(cname, el.Name.LocalName, a.Name.LocalName, Dbl(a.Value)));
            }
            return new WeatherConfig(reset, enable, knobs);
        }
        catch { return new WeatherConfig(false, true, System.Array.Empty<WeatherKnob>()); }
    }

    public static XDocument ParseDoc(string xml) => CeXml.ParseDoc(xml);

    public static bool SetToggle(XDocument doc, string name, bool value)
    {
        if (doc.Root is not { } root || name is not ("reset" or "enable")) return false;
        root.SetAttributeValue(name, value ? "1" : "0");
        return true;
    }

    /// <summary>Set a numeric knob. <paramref name="element"/> = "" targets an attribute on the channel element
    /// itself (e.g. storm); otherwise the named sub-element (created if missing).</summary>
    public static bool SetKnob(XDocument doc, string channel, string element, string attr, double value)
    {
        if (doc.Root is not { } root || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(attr)) return false;
        var ch = root.Element(channel);
        if (ch is null) return false;
        if (element.Length == 0) { ch.SetAttributeValue(attr, Str(value)); return true; }
        var el = ch.Element(element);
        if (el is null) { el = new XElement(element); ch.Add(el); }
        el.SetAttributeValue(attr, Str(value));
        return true;
    }

    public static string ToXml(XDocument doc) => CeXml.Serialize(doc);
}
