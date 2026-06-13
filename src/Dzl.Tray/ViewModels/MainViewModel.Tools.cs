using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Build;
using Dzl.Core.Env;
using Dzl.Core.Tools;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // --- Tools page --------------------------------------------------------

    /// <summary>Discovered DayZ Tools entries (cards on the Tools page).</summary>
    public ObservableCollection<ToolEntry> Tools { get; } = new();

    [ObservableProperty] private bool _workDriveMounted;

    /// <summary>Human-readable work-drive status for the Tools card.</summary>
    public string WorkDriveStatus => WorkDriveMounted ? "P: mounted ✓" : "P: not mounted ✗";

    partial void OnWorkDriveMountedChanged(bool value) => OnPropertyChanged(nameof(WorkDriveStatus));

    /// <summary>The configured DayZ Tools path (for the WorkDrive.exe lookup).</summary>
    public string ToolsPath => _cfg.DayzToolsPath;

    /// <summary>The always-live work-drive source folder P: is mounted from / junctions are anchored on:
    /// the explicit config override if set, else auto-derived from DayZ Tools settings.ini
    /// (<c>[ProjectDrive] path=</c>). Null → falls back to the P:\ junction path.</summary>
    private string? WorkDriveSource => EnvDetect.WorkDriveSource(_cfg.WorkDriveSource, _cfg.DayzToolsPath);

    /// <summary>Re-enumerate the tools catalog and refresh the work-drive state. Called on
    /// Tools page show and via the Refresh button.</summary>
    public void RefreshTools()
    {
        Tools.Clear();
        foreach (var t in ToolCatalog.Discover(_cfg.DayzToolsPath)) Tools.Add(t);
        RefreshWorkDrive();
    }

    public void RefreshWorkDrive() => WorkDriveMounted = WorkDrive.IsMounted();

    /// <summary>Launch a tool GUI on a background task (no UI block). Missing exes return false.</summary>
    public void LaunchTool(ToolEntry tool) => Task.Run(() => { try { ToolLauncher.Launch(tool); } catch { } });

    public void MountWorkDrive()
    {
        var exe = Path.Combine(_cfg.DayzToolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
        var source = WorkDriveSource;
        Task.Run(() =>
        {
            try { WorkDrive.Mount(exe, source); } catch { }
            finally { _dispatcher.BeginInvoke(RefreshWorkDrive); }
        });
    }

    public void UnmountWorkDrive()
    {
        var exe = Path.Combine(_cfg.DayzToolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
        Task.Run(() =>
        {
            try { WorkDrive.Unmount(exe); } catch { }
            finally { _dispatcher.BeginInvoke(RefreshWorkDrive); }
        });
    }

    /// <summary>Resolve a CLI-wrappable tool exe path by key, or null if not present.</summary>
    public string? ToolExe(string key) => ToolCatalog.Find(_cfg.DayzToolsPath, key) is { Exists: true } t ? t.ExePath : null;

    /// <summary>Pack a PBO via Addon Builder on a background task; reports the combined output.</summary>
    public Task<PackResult> PackAsync(string addonExe, string src, string dst, string? prefix, string? sign) =>
        Task.Run(() => AddonBuilder.Pack(addonExe, src, dst, clear: true, packOnly: true,
            prefix: string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim(),
            signKey: string.IsNullOrWhiteSpace(sign) ? null : sign.Trim()));

    /// <summary>Plan a batch PAA conversion (suffix warnings) without running the exe.</summary>
    public List<PaaJob> PlanPaa(string dir, bool recursive) => ImageToPaa.PlanFolder(dir, recursive);

    /// <summary>Run a batch PAA conversion on a background task, streaming per-file results.</summary>
    public Task<List<PaaResult>> ConvertPaaAsync(string paaExe, string dir, bool recursive, IProgress<PaaResult>? progress) =>
        Task.Run(() => ImageToPaa.ConvertFolder(paaExe, dir, recursive, progress));

    /// <summary>Unbinarize a .bin via CfgConvert on a background task.</summary>
    public Task<(bool ok, string output)> UnbinarizeAsync(string cfgExe, string binPath, string outCpp) =>
        Task.Run(() => CfgConvert.Unbinarize(cfgExe, binPath, outCpp));
}
