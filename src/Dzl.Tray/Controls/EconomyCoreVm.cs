using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="EconomyCoreEditor"/> control (the Economy "Economy core" tab): edits the mission's
/// <c>db/economy.xml</c> — a per-entity-group board of init/load/respawn/save toggles. economy.xml is a CLOSED
/// engine vocabulary (see <see cref="EconomyCatalog"/>), so standard groups are flagged <c>IsKnown</c>: their
/// flags are editable but they can't be removed (reset-to-default instead), "Add" offers only known-missing
/// groups, and only a custom/non-standard key may be deleted. Per-tab undo/redo + status come from
/// <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class EconomyCoreVm : RawXmlEditorVm
{
    private readonly EconomyService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspend;

    public EconomyCoreVm(string configPath, Func<string, bool> confirm)
        : this(new EconomyService(configPath), confirm) { }

    private EconomyCoreVm(EconomyService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.EconomyPath,
               "(no db/economy.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    /// <summary>Entity-group rows (each four lifecycle toggles).</summary>
    public ObservableCollection<EconomyGroupVm> Groups { get; } = new();

    /// <summary>Standard engine groups absent from the file — the only names the "Add" affordance offers.</summary>
    public ObservableCollection<string> MissingKnown { get; } = new();

    [ObservableProperty] private string? _selectedMissing;

    public bool HasMissingKnown => MissingKnown.Count > 0;

    protected override void ReloadView()
    {
        _suspend = true;
        try
        {
            foreach (var g in Groups) g.FlagChanged -= OnFlagChanged;
            Groups.Clear();
            foreach (var grp in _svc.Load())
            {
                var vm = new EconomyGroupVm(grp.Name)
                {
                    Init = grp.Init, Load = grp.Load, Respawn = grp.Respawn, Save = grp.Save,
                };
                vm.FlagChanged += OnFlagChanged;
                Groups.Add(vm);
            }
        }
        finally { _suspend = false; }

        RefreshMissingKnown();
    }

    private void RefreshMissingKnown()
    {
        var present = new HashSet<string>(Groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        MissingKnown.Clear();
        foreach (var d in EconomyCatalog.All)
            if (!present.Contains(d.Name)) MissingKnown.Add(d.Name);
        SelectedMissing = MissingKnown.FirstOrDefault();
        OnPropertyChanged(nameof(HasMissingKnown));
    }

    private void OnFlagChanged(EconomyGroupVm g, string flag)
    {
        if (_suspend) return;
        PushUndo();
        Report(_svc.SetFlag(g.Name, flag, g.Get(flag)));
    }

    /// <summary>Add the known-but-missing group picked in the dropdown, seeded with its vanilla defaults.</summary>
    public void AddKnown()
    {
        var name = (SelectedMissing ?? "").Trim();
        if (name.Length == 0) { Status = "✗ pick a standard group to add"; return; }
        if (EconomyCatalog.Find(name) is not { } def)
        { Status = $"✗ \"{name}\" is not a known economy group"; return; }
        if (Groups.Any(g => string.Equals(g.Name, def.Name, StringComparison.OrdinalIgnoreCase)))
        { Status = $"✗ \"{def.Name}\" is already present"; return; }

        PushUndo();
        if (Report(_svc.SetGroup(def.Name, def.Init, def.Load, def.Respawn, def.Save))) ReloadView();
    }

    /// <summary>Reset a standard group's flags to their engine defaults (the non-destructive alternative to
    /// removing it — a missing group already falls back to these defaults).</summary>
    public void ResetToDefault(EconomyGroupVm? row)
    {
        if (row is null) { Status = "✗ select a group to reset"; return; }
        if (EconomyCatalog.Find(row.Name) is not { } def)
        { Status = $"✗ \"{row.Name}\" is not a standard group — it has no engine default"; return; }
        PushUndo();
        if (Report(_svc.SetGroup(def.Name, def.Init, def.Load, def.Respawn, def.Save)))
        {
            ReloadView();
            Status = $"✓ {def.Display} reset to default";
        }
    }

    /// <summary>Remove a group. Standard engine groups are NOT removable (reset instead); only a custom key can go.</summary>
    public void RemoveGroup(EconomyGroupVm? row)
    {
        if (row is null) { Status = "✗ select a group to remove"; return; }
        if (row.IsKnown)
        { Status = $"✗ \"{row.Name}\" is a standard economy group — reset it to default instead of removing it"; return; }
        if (!_confirm($"Remove the custom economy group \"{row.Name}\"?")) return;

        PushUndo();
        if (Report(_svc.RemoveGroup(row.Name))) ReloadView();
    }
}

/// <summary>One economy-group row: a friendly label + the four lifecycle toggles. Standard (engine) groups are
/// <see cref="IsKnown"/> (not removable); a custom key is removable. Toggling a flag raises <see cref="FlagChanged"/>
/// so the VM persists just that flag.</summary>
public sealed partial class EconomyGroupVm : ObservableObject
{
    public EconomyGroupVm(string name)
    {
        Name = name;
        var def = EconomyCatalog.Find(name);
        IsKnown = def is not null;
        Display = def?.Display ?? name;
        Description = def?.Description ?? "Custom (non-standard) economy group.";
    }

    public string Name { get; }
    public string Display { get; }
    public string Description { get; }
    public bool IsKnown { get; }
    public bool IsCustom => !IsKnown;

    public event Action<EconomyGroupVm, string>? FlagChanged;

    [ObservableProperty] private bool _init;
    [ObservableProperty] private bool _load;
    [ObservableProperty] private bool _respawn;
    [ObservableProperty] private bool _save;

    partial void OnInitChanged(bool value) => FlagChanged?.Invoke(this, "init");
    partial void OnLoadChanged(bool value) => FlagChanged?.Invoke(this, "load");
    partial void OnRespawnChanged(bool value) => FlagChanged?.Invoke(this, "respawn");
    partial void OnSaveChanged(bool value) => FlagChanged?.Invoke(this, "save");

    /// <summary>Current value of one lifecycle flag by name (for the VM's persist call).</summary>
    public bool Get(string flag) => flag switch
    {
        "init" => Init,
        "load" => Load,
        "respawn" => Respawn,
        "save" => Save,
        _ => false,
    };
}
