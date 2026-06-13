using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Config;
using Dzl.Core.Launch;
using Dzl.Core.Mods;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _serverArgv = "";
    [ObservableProperty] private string _clientArgv = "";
    [ObservableProperty] private string _modFilter = "";
    [ObservableProperty] private ModRowVm? _selectedMod;

    public ObservableCollection<ModRowVm> Mods { get; } = new();

    /// <summary>Enabled mods that run on the SERVER (side in {both, server}), formatted as the
    /// mod Name with a "(side)" suffix when the side isn't "both" (e.g. "@Admin (server)").
    /// Drives the Dashboard left column's active-mods card. Refreshed by <see cref="RefreshActiveMods"/>.</summary>
    public ObservableCollection<ActiveModVm> ServerMods { get; } = new();

    /// <summary>Enabled mods that run on the CLIENT (side in {both, client}), formatted as the
    /// mod Name with a "(side)" suffix when the side isn't "both" (e.g. "@UI (client)").
    /// Drives the Dashboard right column's active-mods card. Refreshed by <see cref="RefreshActiveMods"/>.</summary>
    public ObservableCollection<ActiveModVm> ClientMods { get; } = new();

    /// <summary>Count of <see cref="ServerMods"/> (for the "Active mods (N)" header).</summary>
    public int ServerModsCount => ServerMods.Count;

    /// <summary>Count of <see cref="ClientMods"/> (for the "Active mods (N)" header).</summary>
    public int ClientModsCount => ClientMods.Count;

    /// <summary>Allowed values for the inline Side combo column (both|server|client).</summary>
    public static IReadOnlyList<string> Sides { get; } = new[] { "both", "server", "client" };

    /// <summary>Summary for the Mods toolbar count label ("N mods, M enabled").</summary>
    public string ModsSummary => $"{Mods.Count} mods, {Mods.Count(m => m.Enabled)} enabled";

    /// <summary>Filtered view over <see cref="Mods"/> bound by the ListBox; reorder
    /// commands still mutate the underlying collection.</summary>
    public ICollectionView ModsView { get; }

    /// <summary>gong-wpf-dragdrop drop handler for the Mods DataGrid (maps view drops back
    /// onto the underlying <see cref="Mods"/> collection). Bound by the grid in XAML.</summary>
    public ModsDropHandler ModsDropHandler { get; }

    /// <summary>True when the Mods page has no usable projects root configured — show the blocking overlay.</summary>
    public bool ModsBlocked => string.IsNullOrWhiteSpace(_cfg.ProjectsRoot);

    /// <summary>Why the Mods page is blocked (shown in the overlay).</summary>
    public string ModsBlockMessage =>
        "Set your projects root first — that's where dzl creates and manages your mod source folders.";

    private void LoadMods()
    {
        foreach (var row in Mods) row.Changed -= OnModChanged;
        Mods.Clear();
        var merged = ModDiscovery.Merge(_cfg.Mods, ModDiscovery.Discover(_cfg.ScanRoots));
        foreach (var m in merged)
        {
            var row = new ModRowVm
            {
                Enabled = m.Enabled,
                Name = m.Name,
                Path = m.Path,
                Side = m.Side,
                Missing = m.Missing,
                Kind = Dzl.Core.Mods.ModClassify.Classify(m.Path, _cfg),
            };
            row.Changed += OnModChanged;
            Mods.Add(row);
        }
        RenumberOrder();
        OnPropertyChanged(nameof(ModsSummary));
    }

    /// <summary>Rewrites each row's 1-based <see cref="ModRowVm.Order"/> from its position
    /// in the underlying <see cref="Mods"/> collection. Called after load and every reorder
    /// so the grid's "#" column matches the real load order.</summary>
    private void RenumberOrder()
    {
        for (int i = 0; i < Mods.Count; i++) Mods[i].Order = i + 1;
    }

    /// <summary>Move a mod within the underlying collection (used by drag-reorder), then
    /// renumber + persist. Indices are into <see cref="Mods"/>, not the filtered view.</summary>
    public void MoveMod(int from, int to)
    {
        if (from < 0 || from >= Mods.Count) return;
        if (to < 0 || to >= Mods.Count || to == from) return;
        Mods.Move(from, to);
        Persist();
    }

    private void OnModChanged()
    {
        if (_suppressPersist) return;
        Persist();
    }

    // --- Persist / preview -------------------------------------------------

    private void Persist()
    {
        var entries = Mods.Select(m => new ModEntry
        {
            Path = m.Path,
            Enabled = m.Enabled,
            Side = m.Side,
        }).ToList();
        _cfg = _cfg with { Mods = entries, Mode = Mode };
        Profiles.Save(_cfg, ActiveName, _configPath);   // mods + mode are per-server
        // Reorder commands mutate the ObservableCollection via Move(); renumber the
        // "#" column and refresh the filtered view so its ordering stays in sync.
        RenumberOrder();
        ModsView.Refresh();
        OnPropertyChanged(nameof(ModsSummary));
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        ServerArgv = ProcessManager.ServerExe(_cfg, Mode) + " " + string.Join(" ", ArgvBuilder.Build(Mode, "server", _cfg));
        ClientArgv = ProcessManager.ClientExe(_cfg, Mode) + " " + string.Join(" ", ArgvBuilder.Build(Mode, "client", _cfg));
        RefreshActiveMods();
    }

    /// <summary>Rebuild the per-target active-mod lists from the live <see cref="Mods"/> rows
    /// (enabled + side). Server runs side in {both, server}; client runs side in {both, client}.
    /// A "(side)" suffix is appended when the side isn't "both". Clears+repopulates on the UI thread.</summary>
    private void RefreshActiveMods()
    {
        void Rebuild()
        {
            ServerMods.Clear();
            ClientMods.Clear();
            foreach (var m in Mods)
            {
                if (!m.Enabled) continue;
                var label = m.Side == "both" ? m.Name : $"{m.Name} ({m.Side})";
                if (m.Side is "both" or "server") ServerMods.Add(new ActiveModVm(label, m.Kind));
                if (m.Side is "both" or "client") ClientMods.Add(new ActiveModVm(label, m.Kind));
            }
            OnPropertyChanged(nameof(ServerModsCount));
            OnPropertyChanged(nameof(ClientModsCount));
        }

        if (_dispatcher.CheckAccess()) Rebuild();
        else _dispatcher.BeginInvoke(Rebuild);
    }

    // --- Mod filter --------------------------------------------------------

    private bool FilterMod(object obj)
    {
        if (string.IsNullOrEmpty(ModFilter)) return true;
        if (obj is not ModRowVm m) return true;
        return (m.Name?.Contains(ModFilter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (m.Path?.Contains(ModFilter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnModFilterChanged(string value) => ModsView.Refresh();

    // --- Mod ordering / side ----------------------------------------------

    private int SelIndex => SelectedMod is null ? -1 : Mods.IndexOf(SelectedMod);

    [RelayCommand]
    private void MoveUp()
    {
        var i = SelIndex;
        if (i <= 0) return;
        Mods.Move(i, i - 1);
        Persist();
    }

    [RelayCommand]
    private void MoveDown()
    {
        var i = SelIndex;
        if (i < 0 || i >= Mods.Count - 1) return;
        Mods.Move(i, i + 1);
        Persist();
    }

    [RelayCommand]
    private void ToTop()
    {
        var i = SelIndex;
        if (i <= 0) return;
        Mods.Move(i, 0);
        Persist();
    }

    [RelayCommand]
    private void ToBottom()
    {
        var i = SelIndex;
        if (i < 0 || i >= Mods.Count - 1) return;
        Mods.Move(i, Mods.Count - 1);
        Persist();
    }

    [RelayCommand]
    private void CycleSide()
    {
        if (SelectedMod is null) return;
        SelectedMod.Side = SelectedMod.Side switch
        {
            "both" => "server",
            "server" => "client",
            _ => "both",
        };
        // ModRowVm.Changed -> OnModChanged -> Persist already fires.
    }

    [RelayCommand]
    private void Rescan()
    {
        _suppressPersist = true;
        try { LoadMods(); }
        finally { _suppressPersist = false; }
        RefreshPreview();
    }

    /// <summary>Drop a mod from the active server's run-list (used to prune stale/"missing" entries that a
    /// Rescan can't touch — Rescan only re-discovers files on disk, it doesn't edit the saved run-list).</summary>
    [RelayCommand]
    private void RemoveMod(ModRowVm? row)
    {
        if (row is null) return;
        row.Changed -= OnModChanged;
        Mods.Remove(row);
        Persist();   // rebuilds the run-list from the remaining rows + saves to the active instance
    }
}
