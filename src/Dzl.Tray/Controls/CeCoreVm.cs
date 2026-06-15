using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Core.Economy;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="CeCoreEditor"/> control (Economy "CE Config" tab): edits the mission's
/// <c>cfgeconomycore.xml</c> — the CE master config. Three surfaces: the custom-file ROUTING manifest
/// (register/unregister the files the other tabs add — they don't load until routed here), the documented
/// default knobs (grouped, validated), and a read-only list of root classes (advanced). Per-tab undo/redo +
/// status from <see cref="RawXmlEditorVm"/>.
/// </summary>
public sealed partial class CeCoreVm : RawXmlEditorVm
{
    private readonly CeCoreService _svc;
    private readonly Func<string, bool> _confirm;
    private bool _suspend;

    public CeCoreVm(string configPath, Func<string, bool> confirm)
        : this(new CeCoreService(configPath), confirm) { }

    private CeCoreVm(CeCoreService svc, Func<string, bool> confirm)
        : base(svc.ReadRaw, svc.WriteRaw, svc.CeCorePath,
               "(no cfgeconomycore.xml — pick/scaffold a server mission)", confirm)
    {
        _svc = svc;
        _confirm = confirm;
    }

    // ── routing manifest ─────────────────────────────────────────────────
    public ObservableCollection<CeRoutedFile> Files { get; } = new();

    public IReadOnlyList<string> FileTypes => CeCoreXml.FileTypes;

    [ObservableProperty] private string _newFileFolder = "";
    [ObservableProperty] private string _newFileName = "";
    [ObservableProperty] private string _newFileType = "types";

    public bool HasFiles => Files.Count > 0;

    // ── default knobs (grouped + validated) ──────────────────────────────
    /// <summary>Flat list of all default knobs (source of truth + persistence wiring).</summary>
    public ObservableCollection<CeDefaultVm> Defaults { get; } = new();

    /// <summary>The same knobs grouped for the editor (Dynamic zones / Logging / Startup / Other).</summary>
    public ObservableCollection<CeDefaultGroup> DefaultGroups { get; } = new();

    public ObservableCollection<string> MissingDefaults { get; } = new();
    [ObservableProperty] private string? _selectedMissingDefault;
    public bool HasMissingDefaults => MissingDefaults.Count > 0;

    // ── root classes (read-only, advanced) ───────────────────────────────
    public ObservableCollection<CeRootClass> RootClasses { get; } = new();

    protected override void ReloadView()
    {
        _suspend = true;
        try
        {
            foreach (var d in Defaults) d.Edited -= OnDefaultEdited;
            Files.Clear();
            Defaults.Clear();
            DefaultGroups.Clear();
            RootClasses.Clear();

            var cfg = _svc.Load();
            foreach (var f in cfg.Files) Files.Add(f);
            foreach (var r in cfg.RootClasses) RootClasses.Add(r);
            foreach (var d in cfg.Defaults)
            {
                var vm = new CeDefaultVm(d.Name, d.Value);
                vm.Edited += OnDefaultEdited;
                Defaults.Add(vm);
            }

            // Group for the editor in a stable order; "Other" last.
            string[] order = { CeCoreDefaults.Zones, CeCoreDefaults.Logging, CeCoreDefaults.Startup };
            foreach (var g in Defaults.GroupBy(d => d.Group)
                                      .OrderBy(g => Array.IndexOf(order, g.Key) is var i && i >= 0 ? i : int.MaxValue))
                DefaultGroups.Add(new CeDefaultGroup(g.Key, new ObservableCollection<CeDefaultVm>(g)));
        }
        finally { _suspend = false; }

        RefreshMissingDefaults();
        OnPropertyChanged(nameof(HasFiles));
    }

    private void RefreshMissingDefaults()
    {
        var present = new HashSet<string>(Defaults.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        MissingDefaults.Clear();
        foreach (var d in CeCoreDefaults.All)
            if (!present.Contains(d.Name)) MissingDefaults.Add(d.Name);
        SelectedMissingDefault = MissingDefaults.FirstOrDefault();
        OnPropertyChanged(nameof(HasMissingDefaults));
    }

    private static bool TryNum(string raw) =>
        double.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private void OnDefaultEdited(CeDefaultVm d)
    {
        if (_suspend) return;
        var value = d.IsBool ? (d.Flag ? "true" : "false") : (d.Text ?? "").Trim();
        if (!d.IsBool && !TryNum(value)) { Status = $"✗ {d.Name} must be a number"; ReloadView(); return; }
        PushUndo();
        Report(_svc.SetDefault(d.Name, value));
    }

    /// <summary>Add a documented default not yet present, seeded with its engine default value.</summary>
    public void AddMissingDefault()
    {
        var name = (SelectedMissingDefault ?? "").Trim();
        if (name.Length == 0) { Status = "✗ pick a default to add"; return; }
        if (CeCoreDefaults.Find(name) is not { } def) { Status = $"✗ \"{name}\" is not a known default"; return; }
        PushUndo();
        if (Report(_svc.SetDefault(def.Name, def.Default))) ReloadView();
    }

    /// <summary>Register a custom CE file from the add form.</summary>
    public void AddFile()
    {
        var folder = (NewFileFolder ?? "").Trim();
        var name = (NewFileName ?? "").Trim();
        if (folder.Length == 0) { Status = "✗ folder must not be empty"; return; }
        if (name.Length == 0) { Status = "✗ file name must not be empty"; return; }
        PushUndo();
        if (Report(_svc.AddFile(folder, name, NewFileType ?? "types")))
        {
            NewFileName = "";
            ReloadView();
        }
    }

    public void RemoveFile(CeRoutedFile? file)
    {
        if (file is null) { Status = "✗ select a file to remove"; return; }
        if (!_confirm($"Unregister \"{file.Folder}/{file.Name}\" from cfgeconomycore?")) return;
        PushUndo();
        if (Report(_svc.RemoveFile(file.Folder, file.Name))) ReloadView();
    }
}

/// <summary>A named group of default knobs for the editor (e.g. "CE logging").</summary>
public sealed record CeDefaultGroup(string Header, ObservableCollection<CeDefaultVm> Items);

/// <summary>One editable cfgeconomycore default knob. Booleans (from the catalog or a true/false value) bind
/// <see cref="Flag"/>; everything else binds <see cref="Text"/> (numeric, validated on commit). Carries its
/// <see cref="Group"/> + <see cref="Description"/> for the grouped editor.</summary>
public sealed partial class CeDefaultVm : ObservableObject
{
    public CeDefaultVm(string name, string value)
    {
        Name = name;
        var def = CeCoreDefaults.Find(name);
        IsBool = def?.IsBool ?? IsBoolValue(value);
        Group = def?.Group ?? "Other";
        Description = def?.Description ?? "";
        if (IsBool) _flag = IsTrue(value);
        else _text = value;
    }

    public string Name { get; }
    public string Group { get; }
    public string Description { get; }
    public bool IsBool { get; }

    public event Action<CeDefaultVm>? Edited;

    [ObservableProperty] private bool _flag;
    [ObservableProperty] private string _text = "";

    partial void OnFlagChanged(bool value) { if (IsBool) Edited?.Invoke(this); }

    /// <summary>Commit a numeric edit (LostFocus / Enter).</summary>
    public void Commit() { if (!IsBool) Edited?.Invoke(this); }

    private static bool IsBoolValue(string v) =>
        string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(v?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(string v) => string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
