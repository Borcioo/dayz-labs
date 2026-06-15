using System.Collections.ObjectModel;
using System.IO;
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
    private CeWorld? _world;

    // confirm is accepted for ctor consistency with the other CE tab VMs; the dashboard no longer prompts —
    // the "disable unused presets" action moved to the Random Presets tab.
    public CeDashboardVm(string configPath, Func<string, bool> confirm)
    {
        _configPath = configPath;
        _ = confirm;
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

    /// <summary>Refresh the cheap tiles, then auto-run the full cross-file validation when the mission's CE
    /// files have changed since the last run (or were never validated this session). Switching back to the
    /// tab with nothing changed is a no-op — no re-run, no progress-bar flash, no cost on large missions.
    /// The panel fires this and forgets it (UI boundary); tests await it.</summary>
    public async Task RefreshAndValidateAsync()
    {
        Refresh();
        if (_world is { HasMission: true } && !IsValidating && FileSignature(_world) != _validatedSignature)
            await RunFullValidation();
    }

    /// <summary>A cheap change-token for the mission's CE files: each file's path + last-write time. Two
    /// loads with no on-disk edit produce the same token, so an unchanged re-validation can be skipped.</summary>
    public static string FileSignature(CeWorld world) =>
        string.Join("|", world.Files
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Path}={(f.Exists && File.Exists(f.Path) ? File.GetLastWriteTimeUtc(f.Path).Ticks : 0)}"));

    /// <summary>The CE-file signature the last full validation ran against; auto-validation skips while it's
    /// unchanged. Null until the first validation.</summary>
    private string? _validatedSignature;

    /// <summary>How many full validations have run this session — a test seam for the staleness guard, and
    /// harmless diagnostics otherwise.</summary>
    public int ValidationRunCount { get; private set; }

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
            _validatedSignature = FileSignature(world);   // mark this file state validated (gates auto-rerun)
            ValidationRunCount++;
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

}
