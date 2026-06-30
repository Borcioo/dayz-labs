using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Logs;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // --- Logs page state --------------------------------------------------
    // The four live log panes (script/rpt/adm/client) modeled as a reorderable collection
    // so every view mode binds the same items. Order is presentation-only (not persisted).
    public ObservableCollection<LogPaneVm> LogPanes { get; } = new();

    /// <summary>Active Logs layout: "grid" | "list" | "tabs" | "focus".</summary>
    [ObservableProperty] private string _logsViewMode = "grid";

    /// <summary>The pane shown in Focus mode (and a quick-jump in other modes).</summary>
    [ObservableProperty] private LogPaneVm? _selectedLogPane;

    /// <summary>gong-wpf-dragdrop drop handler for reordering <see cref="LogPanes"/>.</summary>
    public LogsDropHandler LogsDropHandler { get; }

    // --- Live log tailing --------------------------------------------------
    // Logs are per-server: they come from the active instance's ProfilesPath/ClientProfilesPath.
    // Switching the active server re-points them via RetailLogs() (called from Reload()).

    private void StartLogTails()
    {
        var paths = LogResolver.Resolve(_cfg.ProfilesPath, _cfg.ClientProfilesPath);
        foreach (var pane in LogPanes)
        {
            var path = paths.GetValueOrDefault(pane.Key);
            pane.Path = path;
            pane.FileName = string.IsNullOrEmpty(path) ? "(none)" : Path.GetFileName(path);
            Tail(pane, path);
        }
    }

    /// <summary>
    /// Poll (from the status timer) for the newest log file changing — e.g. a server just started and
    /// wrote a fresh script/RPT/ADM, or the active server changed. When any pane's resolved newest
    /// file differs from what it's tailing, re-tail so the panes follow the live files. No-op otherwise.
    /// </summary>
    private void RefreshLogPaths()
    {
        if (_disposed) return;
        var paths = LogResolver.Resolve(_cfg.ProfilesPath, _cfg.ClientProfilesPath);
        foreach (var pane in LogPanes)
        {
            var p = paths.GetValueOrDefault(pane.Key);
            if (!string.Equals(p, pane.Path, StringComparison.OrdinalIgnoreCase)) { RetailLogs(); return; }
        }
    }

    /// <summary>Cancel the current log follows and restart them against the active server's profiles
    /// (clears each pane first so you don't see the previous server's lines).</summary>
    private void RetailLogs()
    {
        var old = _logCts;
        _logCts = new CancellationTokenSource();
        old.Cancel();
        // Don't Dispose() the old CTS here: background Follow tasks may still hold its token for a
        // moment (Task.Delay/registrations) and disposing under them races into ObjectDisposedException.
        // Cancellation stops the loops; the orphaned CTS is collected by GC.
        foreach (var pane in LogPanes) pane.Clear();
        StartLogTails();
    }

    private void Tail(LogPaneVm pane, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        // Seed with the existing tail so the pane isn't empty (one batch).
        pane.AppendBatch(LogTail.LastLines(path, 200));
        // Run the follow loop on a background thread (Task.Run) so file reads never touch the UI
        // thread. New lines arrive BATCHED per poll cycle → one dispatcher hop per cycle (not per
        // line), so a server's startup spew can't flood/freeze the UI.
        var token = _logCts.Token;
        _ = Task.Run(() => LogTail.Follow(path,
            batch => _dispatcher.BeginInvoke(() => pane.AppendBatch(batch)), token));
    }

    /// <summary>Switch the Logs page layout (grid/list/tabs/focus).</summary>
    [RelayCommand]
    private void SetLogsView(string mode) => LogsViewMode = mode;

    /// <summary>Open the folder containing a pane's resolved log file in Explorer.</summary>
    [RelayCommand]
    private void OpenLogFolder(LogPaneVm? pane)
    {
        var path = pane?.Path;
        if (string.IsNullOrEmpty(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        Dzl.Tray.ShellOpen.Folder(dir);
    }

    /// <summary>Clear a pane's in-memory view (the underlying file is untouched).</summary>
    [RelayCommand]
    private void ClearLog(LogPaneVm? pane) => pane?.Clear();

    /// <summary>Open the pane's resolved log file in the configured editor (its folder as the
    /// workspace). Falls back to the OS default app when no editor is set.</summary>
    [RelayCommand]
    private void OpenLogInEditor(LogPaneVm? pane)
    {
        var path = pane?.Path;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        Dzl.Core.Tools.EditorLauncher.OpenFile(Cfg.EditorPath, path, 0, Path.GetDirectoryName(path));
    }

    /// <summary>Open a clicked stack-trace reference (<c>file.c : line</c>) in the configured editor. Absolute
    /// paths open directly; relative script paths are resolved best-effort against the projects root and the
    /// mounted P: work drive (where vanilla + mod scripts live). Silently no-ops when nothing resolves.</summary>
    public void OpenLogFileRef(LogPaneVm pane, string path, int line)
    {
        var resolved = ResolveLogPath(pane, path);
        if (resolved is null) return;
        Dzl.Core.Tools.EditorLauncher.OpenFile(Cfg.EditorPath, resolved, line, Path.GetDirectoryName(resolved));
    }

    private string? ResolveLogPath(LogPaneVm pane, string path)
    {
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
        var rel = path.Replace('/', Path.DirectorySeparatorChar);
        var bases = new[] { Cfg.ProjectsRoot, @"P:\", Path.GetDirectoryName(pane.Path) };
        foreach (var b in bases)
        {
            if (string.IsNullOrEmpty(b)) continue;
            var cand = Path.Combine(b, rel);
            if (File.Exists(cand)) return cand;
        }
        return null;
    }

    /// <summary>Open the pane's log file in a PowerShell window that live-tails it
    /// (<c>Get-Content -Wait</c>) — a real terminal view for grepping/following.</summary>
    [RelayCommand]
    private void OpenLogTerminal(LogPaneVm? pane)
    {
        var path = pane?.Path;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            // Single-quote the path (PowerShell literal); double any embedded quote.
            var escaped = path.Replace("'", "''");
            var args = $"-NoExit -Command \"Get-Content -LiteralPath '{escaped}' -Tail 200 -Wait\"";
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("powershell.exe", args) { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }
}
