using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Build;
using Dzl.Core.Build.Preflight;
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

    [RelayCommand]
    public void RefreshWorkDrive() => WorkDriveMounted = WorkDrive.IsMounted();

    /// <summary>Launch a tool GUI on a background task (no UI block). Missing exes return false.</summary>
    [RelayCommand]
    public void LaunchTool(ToolEntry tool) => Task.Run(() => { try { ToolLauncher.Launch(tool); } catch { } });

    [ObservableProperty] private bool _extractingGame;
    [ObservableProperty] private string _extractGameStatus = "";

    /// <summary>True when no extraction is in flight (drives the button's enabled state).</summary>
    public bool CanExtractGame => !ExtractingGame;
    partial void OnExtractingGameChanged(bool value) => OnPropertyChanged(nameof(CanExtractGame));

    /// <summary>Reliable in-app game-data extraction: unpack every vanilla PBO to P: via BankRev (replaces
    /// the flaky WorkDrive.exe /ExtractGameData). Incremental; <paramref name="force"/> re-extracts all.</summary>
    [RelayCommand]
    public async Task ExtractGameData(object? force)
    {
        if (ExtractingGame) return;
        var full = force is bool b && b;
        var bankrev = ToolExe("bankrev");
        if (bankrev is null) { ExtractGameStatus = "BankRev.exe not found — set the DayZ Tools path"; return; }
        if (!WorkDrive.IsMounted()) { ExtractGameStatus = "P: not mounted — mount it first"; return; }

        ExtractingGame = true;
        ExtractGameStatus = full ? "full re-extract starting…" : "starting…";
        var game = _cfg.DayzPath;
        try
        {
            var r = await Task.Run(() => GameUnpack.UnpackAll(bankrev, game, @"P:\", full,
                onItem: it => _dispatcher.BeginInvoke(() =>
                    ExtractGameStatus = $"[{it.Index}/{it.Total}] {Path.GetFileName(it.Pbo)} — {it.Status}")));
            ExtractGameStatus = (r.Ok ? "✓ " : "✗ ") + r.Message;
            RefreshWorkDrive();
        }
        finally { ExtractingGame = false; }
    }

    [RelayCommand]
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

    [RelayCommand]
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

    /// <summary>Pack an arbitrary folder with the full project-build pipeline (preflight gate → Binarize →
    /// CfgConvert → in-process pack → sign) — the Tools-page equivalent of a My Mods build, on a user-picked
    /// source/output. Streams the log; runs off the UI thread.</summary>
    public Task<BuildService.PackFolderResult> PackFolderAsync(string source, string output, string? prefix,
        bool binarize, bool sign, string? keyName, bool ignorePreflight, IProgress<string>? log)
    {
        var configPath = _configPath;
        return Task.Run(() => new BuildService(configPath).PackFolder(source, output, prefix, binarize, sign,
            keyName, ignorePreflight, line => log?.Report(line)));
    }

    /// <summary>Preflight an arbitrary source folder (Tools packer) off the UI thread.</summary>
    public Task<PreflightView> PreflightFolderAsync(string source)
    {
        var configPath = _configPath;
        return Task.Run(() => new BuildService(configPath).PreflightFolder(source));
    }

    /// <summary>Configured signing keys (names) + the default to preselect (the configured signing key, else first).</summary>
    public (IReadOnlyList<string> Names, string? Default) SigningKeys()
    {
        try
        {
            var names = new BuildService(_configPath).ListKeys().Select(k => k.Name).ToList();
            var def = !string.IsNullOrWhiteSpace(_cfg.SigningKey) &&
                      names.Any(n => n.Equals(_cfg.SigningKey, System.StringComparison.OrdinalIgnoreCase))
                ? names.First(n => n.Equals(_cfg.SigningKey, System.StringComparison.OrdinalIgnoreCase))
                : names.FirstOrDefault();
            return (names, def);
        }
        catch { return (System.Array.Empty<string>(), null); }
    }

    /// <summary>Plan a batch PAA conversion (suffix warnings) without running the exe.</summary>
    public List<PaaJob> PlanPaa(string dir, bool recursive) => ImageToPaa.PlanFolder(dir, recursive);

    /// <summary>Run a batch PAA conversion on a background task, streaming per-file results.</summary>
    public Task<List<PaaResult>> ConvertPaaAsync(string paaExe, string dir, bool recursive, IProgress<PaaResult>? progress) =>
        Task.Run(() => ImageToPaa.ConvertFolder(paaExe, dir, recursive, progress));

    /// <summary>Unbinarize a .bin via CfgConvert on a background task.</summary>
    public Task<(bool ok, string output)> UnbinarizeAsync(string cfgExe, string binPath, string outCpp) =>
        Task.Run(() => CfgConvert.Unbinarize(cfgExe, binPath, outCpp));
}
