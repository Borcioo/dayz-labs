using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Economy;
using Dzl.Core.Economy.Lint;

namespace Dzl.Tray.Controls;

/// <summary>One stat tile on the Economy dashboard. Clicking it navigates to that file's editor tab.
/// <c>Issues</c>/<c>HasErrors</c> are filled after a full validation to badge the tile.</summary>
public sealed record CeStat(string Title, string Value, string Detail, CeKind Kind, bool Missing,
    int Issues = 0, bool HasErrors = false);

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
    private readonly RandomPresetsService _presets;
    private readonly Func<string, bool> _confirm;
    private CeWorld? _world;

    public CeDashboardVm(string configPath, Func<string, bool> confirm)
    {
        _configPath = configPath;
        _confirm = confirm;
        _presets = new RandomPresetsService(configPath);
    }

    /// <summary>Raised when a tile or finding is clicked — the host panel selects that file's tab and,
    /// when an entry is given (a finding click), filters that editor's list to it.</summary>
    public event Action<CeKind, string>? NavigateRequested;

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
        BuildStats(_world, _lastFindings);
    }

    private IReadOnlyList<LintFinding> _lastFindings = Array.Empty<LintFinding>();

    private void BuildStats(CeWorld w, IReadOnlyList<LintFinding> findings)
    {
        Stats.Clear();
        var types = w.Types.Entries;
        var vanilla = types.Count(t => t.SourceFile.Contains("db", StringComparison.OrdinalIgnoreCase));
        Stats.Add(Badge(new("Types", types.Count.ToString("N0"),
            $"{types.Count - vanilla:N0} in custom/mod files", CeKind.Types, Missing(w, CeKind.Types) && types.Count == 0), findings));
        Stats.Add(Tile("Events", w.Events.Count, "events", CeKind.Events, w, findings));
        Stats.Add(Tile("Globals", w.Globals.Count, "vars", CeKind.Globals, w, findings));
        Stats.Add(Tile("Spawnable Types", w.SpawnableTypes.Count, "types", CeKind.SpawnableTypes, w, findings));
        Stats.Add(Tile("Random Presets", w.RandomPresets.Count, "presets", CeKind.RandomPresets, w, findings));

        var groups = w.PlayerSpawns.Sum(c => c.Bubbles.Sum(b => b.Groups.Count));
        Stats.Add(Badge(new("Player Spawns", w.PlayerSpawns.Count.ToString(),
            $"{groups} position group(s)", CeKind.PlayerSpawns, Missing(w, CeKind.PlayerSpawns)), findings));

        var dictNames = w.Limits.Usage.Count + w.Limits.Value.Count + w.Limits.Tag.Count + w.Limits.Category.Count;
        Stats.Add(Badge(new("Dictionaries", dictNames.ToString(),
            "usage / value / tag / category", CeKind.Dictionaries, dictNames == 0), findings));
    }

    private static CeStat Tile(string title, int count, string unit, CeKind kind, CeWorld w,
                              IReadOnlyList<LintFinding> findings) =>
        Badge(new(title, count.ToString("N0"), Missing(w, kind) ? "file not found" : $"{count:N0} {unit}", kind, Missing(w, kind)), findings);

    /// <summary>Stamp a tile with its error+warning count (and whether any are errors) from the last run.</summary>
    private static CeStat Badge(CeStat s, IReadOnlyList<LintFinding> findings)
    {
        var mine = findings.Where(f => f.Kind == s.Kind).ToList();
        var issues = mine.Count(f => f.Severity != LintSeverity.Info);
        return s with { Issues = issues, HasErrors = mine.Any(f => f.Severity == LintSeverity.Error) };
    }

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
            _lastFindings = findings;

            Findings.Clear();
            foreach (var f in findings.OrderBy(f => f.Severity).ThenBy(f => f.File))
                Findings.Add(new CeFindingRow(f.Severity, f.Message, f.File, f.EntryName, f.Kind));

            ErrorCount = findings.Count(f => f.Severity == LintSeverity.Error);
            WarningCount = findings.Count(f => f.Severity == LintSeverity.Warning);
            InfoCount = findings.Count(f => f.Severity == LintSeverity.Info);
            Summary = findings.Count == 0
                ? "✓ No problems found."
                : $"{ErrorCount} error(s) · {WarningCount} warning(s) · {InfoCount} info";

            BuildStats(world, findings);   // re-stamp the tiles with per-file issue badges
        }
        finally
        {
            IsValidating = false;
            Progress = 100;
        }
    }

    /// <summary>Tile click — jump to the file's editor, no filter.</summary>
    [RelayCommand]
    private void Navigate(CeKind kind) => NavigateRequested?.Invoke(kind, "");

    /// <summary>Finding click — jump to the file's editor and filter its list to the finding's entry.</summary>
    [RelayCommand]
    private void NavigateFinding(CeFindingRow? row)
    {
        if (row is not null) NavigateRequested?.Invoke(row.Kind, row.Entry);
    }

    [RelayCommand]
    private void OpenMissionFolder()
    {
        if (HasMission) ShellOpen.Folder(MissionDir);
    }

    /// <summary>Comment out (not delete) every random preset that no spawnabletype references, so dead
    /// presets stop loading without being lost. Confirms first, then re-validates to refresh the report.</summary>
    [RelayCommand]
    private void DisableUnusedPresets()
    {
        var world = _world ??= new CeWorldLoader(_configPath).Load();
        var unused = FindUnusedPresets(world);
        if (unused.Count == 0) { Summary = "✓ No unused presets to disable."; return; }

        if (!_confirm($"Disable (comment out) {unused.Count} unused preset(s)? They stay in the file and can " +
                      "be re-enabled per-row on the Random Presets tab.")) return;

        var done = 0;
        foreach (var (kind, name) in unused)
            if (_presets.DisablePreset(kind, name).ok) done++;

        _world = null; // file changed — drop the cached world so the next pass re-reads it
        Refresh();
        Summary = $"Disabled {done} unused preset(s). Run a full validation to refresh the report.";
    }

    /// <summary>The (kind, name) of every active preset referenced by no spawnabletype of its kind.</summary>
    private static List<(PresetKind Kind, string Name)> FindUnusedPresets(CeWorld world)
    {
        var refdCargo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refdAttach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in world.SpawnableTypes)
            foreach (var b in t.Cargo.Concat(t.Attachments))
                if (b.IsPreset && b.Preset is { } pr)
                    (b.IsAttachments ? refdAttach : refdCargo).Add(pr);

        return world.RandomPresets
            .Where(p => !p.Disabled)
            .Where(p => !(p.Kind == PresetKind.Attachments ? refdAttach : refdCargo).Contains(p.Name))
            .Select(p => (p.Kind, p.Name))
            .ToList();
    }
}
