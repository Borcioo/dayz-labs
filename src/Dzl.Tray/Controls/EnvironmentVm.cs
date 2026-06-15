using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="EnvironmentEditor"/> control (World "Environment" tab): edits cfgenvironment.xml —
/// animal/infected territories. A territory list (master) drives its editable count/radius <c>item</c> knobs +
/// a read-only spawn list (detail). The referenced <c>env/*_territories.xml</c> zone-geometry files are opened
/// externally (shortcuts) rather than hand-edited. Per-tab undo/redo + status from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class EnvironmentVm : RawXmlEditorVm
{
    private readonly EnvironmentService _svc;
    private bool _suspend;

    public EnvironmentVm(string configPath, Func<string, bool> confirm)
        : this(new EnvironmentService(configPath), confirm) { }

    private EnvironmentVm(EnvironmentService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.EnvironmentPath,
               "(no cfgenvironment.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
    }

    private List<EnvTerritory>? _model;
    private List<EnvTerritory> Model => _model ??= _svc.Load().Territories.ToList();
    protected override void InvalidateModelCache() => _model = null;

    public ObservableCollection<EnvTerritoryRowVm> Territories { get; } = new();
    [ObservableProperty] private EnvTerritoryRowVm? _selectedTerritory;

    public ObservableCollection<EnvItemVm> Items { get; } = new();
    public ObservableCollection<EnvSpawn> Spawns { get; } = new();
    public ObservableCollection<EnvFileVm> Files { get; } = new();

    protected override void ReloadView()
    {
        var prev = SelectedTerritory?.Name;
        _model = null;
        Territories.Clear();
        foreach (var t in Model)
            Territories.Add(new EnvTerritoryRowVm(t.Name, t.Type, t.Behavior, t.Items.Count));

        // Env territory files (zone geometry) → external-open shortcuts.
        Files.Clear();
        var dir = _svc.MissionDir();
        foreach (var rel in _svc.Load().Files)
            Files.Add(new EnvFileVm(rel, dir is null ? rel : Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar))));

        SelectedTerritory = Territories.FirstOrDefault(t => string.Equals(t.Name, prev, StringComparison.OrdinalIgnoreCase))
                            ?? Territories.FirstOrDefault();
        LoadDetail();
    }

    partial void OnSelectedTerritoryChanged(EnvTerritoryRowVm? value) => LoadDetail();

    private void LoadDetail()
    {
        _suspend = true;
        try
        {
            foreach (var i in Items) i.Edited -= OnItemEdited;
            Items.Clear();
            Spawns.Clear();
            if (SelectedTerritory is { } sel && Model.FirstOrDefault(t => string.Equals(t.Name, sel.Name, StringComparison.OrdinalIgnoreCase)) is { } t)
            {
                foreach (var it in t.Items)
                {
                    var ivm = new EnvItemVm(it.Name, it.Val);
                    ivm.Edited += OnItemEdited;
                    Items.Add(ivm);
                }
                foreach (var s in t.Spawns) Spawns.Add(s);
            }
        }
        finally { _suspend = false; }
    }

    protected override string? CaptureSelectionToken() => SelectedTerritory?.Name;
    protected override void RestoreSelectionToken(string? token)
    {
        if (token is not null) SelectedTerritory = Territories.FirstOrDefault(t => string.Equals(t.Name, token, StringComparison.OrdinalIgnoreCase)) ?? SelectedTerritory;
    }

    private static bool TryNum(string raw, out double v) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void OnItemEdited(EnvItemVm item)
    {
        if (_suspend || SelectedTerritory is not { } t) return;
        if (!TryNum(item.ValText, out _)) { Status = $"✗ {item.Name} must be a number"; LoadDetail(); return; }
        PushUndo();
        Report(_svc.SetItem(t.Name, item.Name, (item.ValText ?? "").Trim()));
    }
}

/// <summary>One territory row in the master list (name + type + behavior + knob count).</summary>
public sealed record EnvTerritoryRowVm(string Name, string Type, string Behavior, int ItemCount);

/// <summary>One editable territory item knob (count/radius), validated numeric on commit.</summary>
public sealed partial class EnvItemVm : ObservableObject
{
    public EnvItemVm(string name, string val) { Name = name; _valText = val; }
    public string Name { get; }
    [ObservableProperty] private string _valText;
    public void Commit() => Edited?.Invoke(this);
    public event Action<EnvItemVm>? Edited;
}

/// <summary>One referenced env/*_territories.xml file (display name + absolute path) for the open-externally shortcut.</summary>
public sealed record EnvFileVm(string Name, string Path);
