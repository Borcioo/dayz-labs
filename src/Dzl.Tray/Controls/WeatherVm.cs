using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="WeatherEditor"/> control (World "Weather" tab): edits cfgweather.xml — the reset/enable
/// toggles plus per-channel numeric knobs (overcast/fog/rain/wind/snowfall/storm), grouped into cards. All edits
/// route through <see cref="WeatherService"/>; per-tab undo/redo + status from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class WeatherVm : RawXmlEditorVm
{
    private readonly WeatherService _svc;
    private bool _suspend;

    public WeatherVm(string configPath, Func<string, bool> confirm)
        : this(new WeatherService(configPath), confirm) { }

    private WeatherVm(WeatherService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.WeatherPath,
               "(no cfgweather.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
    }

    [ObservableProperty] private bool _resetFlag;
    [ObservableProperty] private bool _enableFlag = true;

    partial void OnResetFlagChanged(bool value) { if (!_suspend) Persist("reset", value); }
    partial void OnEnableFlagChanged(bool value) { if (!_suspend) Persist("enable", value); }

    private void Persist(string toggle, bool value)
    {
        PushUndo();
        Report(_svc.SetToggle(toggle, value));
    }

    public ObservableCollection<WeatherChannelVm> Channels { get; } = new();

    protected override void ReloadView()
    {
        _suspend = true;
        try
        {
            foreach (var ch in Channels) foreach (var k in ch.Knobs) k.Edited -= OnKnobEdited;
            Channels.Clear();

            var cfg = _svc.Load();
            ResetFlag = cfg.Reset;
            EnableFlag = cfg.Enable;

            foreach (var g in cfg.Knobs.GroupBy(k => k.Channel))
            {
                var ch = new WeatherChannelVm(g.Key);
                foreach (var k in g)
                {
                    var kvm = new WeatherKnobVm(k.Channel, k.Element, k.Attr, k.Value);
                    kvm.Edited += OnKnobEdited;
                    ch.Knobs.Add(kvm);
                }
                Channels.Add(ch);
            }
        }
        finally { _suspend = false; }
    }

    private static bool TryNum(string raw, out double v) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void OnKnobEdited(WeatherKnobVm k)
    {
        if (_suspend) return;
        if (!TryNum(k.ValueText, out var v)) { Status = $"✗ {k.Label} must be a number"; ReloadView(); return; }
        PushUndo();
        Report(_svc.SetKnob(k.Channel, k.Element, k.Attr, v));
    }
}

/// <summary>One weather channel card (overcast/fog/rain/wind/snowfall/storm) with its numeric knobs.</summary>
public sealed class WeatherChannelVm
{
    public WeatherChannelVm(string name) => Name = name;
    public string Name { get; }
    public ObservableCollection<WeatherKnobVm> Knobs { get; } = new();
}

/// <summary>One editable weather knob (numeric, validated on commit).</summary>
public sealed partial class WeatherKnobVm : ObservableObject
{
    public WeatherKnobVm(string channel, string element, string attr, double value)
    {
        Channel = channel;
        Element = element;
        Attr = attr;
        _valueText = value.ToString(CultureInfo.InvariantCulture);
        Label = element.Length == 0 ? attr : $"{element} · {attr}";
    }

    public string Channel { get; }
    public string Element { get; }
    public string Attr { get; }
    public string Label { get; }

    [ObservableProperty] private string _valueText;

    public void Commit() => Edited?.Invoke(this);
    public event Action<WeatherKnobVm>? Edited;
}
