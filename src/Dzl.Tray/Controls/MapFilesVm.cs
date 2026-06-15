using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;

namespace Dzl.Tray.Controls;

/// <summary>
/// Backs the <see cref="MapFilesEditor"/> control (Economy "Map files" tab): lists the active mission's
/// auto-generated map-data files (mapgroup*/mapcluster*). These are exported in-game, not hand-edited, so the
/// tab offers only open-externally shortcuts (handled in code-behind via <see cref="ShellOpen"/>) — no editor.
/// </summary>
public sealed partial class MapFilesVm : ObservableObject
{
    private readonly MapFilesService _svc;

    public MapFilesVm(string configPath) => _svc = new MapFilesService(configPath);

    public ObservableCollection<MapFileVm> Files { get; } = new();

    /// <summary>The active mission directory (for the "open mission folder" shortcut), or "" when none.</summary>
    [ObservableProperty] private string _missionDir = "";

    public bool HasFiles => Files.Count > 0;
    public bool HasMission => !string.IsNullOrEmpty(MissionDir);

    public void Reload()
    {
        Files.Clear();
        MissionDir = _svc.MissionDir() ?? "";
        foreach (var f in _svc.Files())
            Files.Add(new MapFileVm(f.Name, f.Path, SizeLabel(f.Bytes), f.Description));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasMission));
    }

    private static string SizeLabel(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:0.0} KB"
        : $"{bytes} B";
}

/// <summary>One map-data file row (display only; actions open it externally).</summary>
public sealed record MapFileVm(string Name, string Path, string Size, string Description);
