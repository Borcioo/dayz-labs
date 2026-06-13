using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;

namespace Dzl.Tray.Controls;

/// <summary>One stat tile on the Economy dashboard. Clicking it navigates to that file's editor tab.</summary>
public sealed record CeStat(string Title, string Value, string Detail, CeKind Kind, bool Missing);

/// <summary>One validation finding row. Clicking it jumps to the owning editor tab.</summary>
public sealed record CeFindingRow(LintSeverity Severity, string Message, string File, string Entry, CeKind Kind)
{
    public string Glyph => Severity switch
    {
        LintSeverity.Error => "✕",
        LintSeverity.Warning => "⚠",
        _ => "ℹ",
    };
}

/// <summary>Backs the Economy "Dashboard" tab: per-file stat tiles + an aggregated validation report.
/// Stats load cheaply when the tab is shown; the heavy cross-file pass runs off the UI thread on the
/// "Run full validation" button with a progress bar. Tile/finding clicks raise
/// <see cref="NavigateRequested"/> so the panel switches to the matching editor.</summary>
public partial class CeDashboardVm : ObservableObject
{
    private readonly string _configPath;
    private CeWorld? _world;

    public CeDashboardVm(string configPath) => _configPath = configPath;

    /// <summary>Raised when a tile or finding is clicked — the host panel selects that file's tab.</summary>
    public event Action<CeKind>? NavigateRequested;

    public ObservableCollection<CeStat> Stats { get; } = new();
    public ObservableCollection<CeFindingRow> Findings { get; } = new();

    [ObservableProperty] private bool _hasMission;
    [ObservableProperty] private string _missionDir = "";
    [ObservableProperty] private string _summary = "Not validated yet — run a full validation.";
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _infoCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanValidate))]
    private bool _isValidating;
    [ObservableProperty] private int _progress;

    /// <summary>False while a full validation is running (drives the button's enabled state).</summary>
    public bool CanValidate => !IsValidating;

    /// <summary>Reload the stat tiles (cheap — counts only). Called when the dashboard tab is shown.</summary>
    public void Refresh()
    {
        _world = new CeWorldLoader(_configPath).Load();
        MissionDir = _world.MissionDir;
        HasMission = _world.HasMission;
        BuildStats(_world);
    }

    private void BuildStats(CeWorld w)
    {
        Stats.Clear();
        var types = w.Types.Entries;
        var vanilla = types.Count(t => t.SourceFile.Contains("db", StringComparison.OrdinalIgnoreCase));
        Stats.Add(new("Types", types.Count.ToString("N0"),
            $"{types.Count - vanilla:N0} in custom/mod files", CeKind.Types, Missing(w, CeKind.Types) && types.Count == 0));
        Stats.Add(Tile("Events", w.Events.Count, "events", CeKind.Events, w));
        Stats.Add(Tile("Globals", w.Globals.Count, "vars", CeKind.Globals, w));
        Stats.Add(Tile("Spawnable Types", w.SpawnableTypes.Count, "types", CeKind.SpawnableTypes, w));
        Stats.Add(Tile("Random Presets", w.RandomPresets.Count, "presets", CeKind.RandomPresets, w));

        var groups = w.PlayerSpawns.Sum(c => c.Bubbles.Sum(b => b.Groups.Count));
        Stats.Add(new("Player Spawns", w.PlayerSpawns.Count.ToString(),
            $"{groups} position group(s)", CeKind.PlayerSpawns, Missing(w, CeKind.PlayerSpawns)));

        var dictNames = w.Limits.Usage.Count + w.Limits.Value.Count + w.Limits.Tag.Count + w.Limits.Category.Count;
        Stats.Add(new("Dictionaries", dictNames.ToString(),
            "usage / value / tag / category", CeKind.Dictionaries, dictNames == 0));
    }

    private static CeStat Tile(string title, int count, string unit, CeKind kind, CeWorld w) =>
        new(title, count.ToString("N0"), Missing(w, kind) ? "file not found" : $"{count:N0} {unit}", kind, Missing(w, kind));

    private static bool Missing(CeWorld w, CeKind kind) =>
        w.Files.FirstOrDefault(f => f.Kind == kind) is { Exists: false };

    [RelayCommand]
    private async Task RunFullValidation()
    {
        if (IsValidating) return;
        IsValidating = true;
        Progress = 0;
        try
        {
            var world = _world ??= await Task.Run(() => new CeWorldLoader(_configPath).Load());
            var progress = new Progress<int>(p => Progress = p);
            var findings = await Task.Run(() => new CeValidator().ValidateFull(world, progress));

            Findings.Clear();
            foreach (var f in findings.OrderBy(f => f.Severity).ThenBy(f => f.File))
                Findings.Add(new CeFindingRow(f.Severity, f.Message, f.File, f.EntryName, f.Kind));

            ErrorCount = findings.Count(f => f.Severity == LintSeverity.Error);
            WarningCount = findings.Count(f => f.Severity == LintSeverity.Warning);
            InfoCount = findings.Count(f => f.Severity == LintSeverity.Info);
            Summary = findings.Count == 0
                ? "✓ No problems found."
                : $"{ErrorCount} error(s) · {WarningCount} warning(s) · {InfoCount} info";
        }
        finally
        {
            IsValidating = false;
            Progress = 100;
        }
    }

    [RelayCommand]
    private void Navigate(CeKind kind) => NavigateRequested?.Invoke(kind);

    [RelayCommand]
    private void OpenMissionFolder()
    {
        if (HasMission) ShellOpen.Folder(MissionDir);
    }
}
