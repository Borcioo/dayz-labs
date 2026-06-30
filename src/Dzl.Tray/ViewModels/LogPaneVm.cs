using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Logs;

namespace Dzl.Tray.ViewModels;

/// <summary>
/// One live log pane (script/rpt/adm/client). Holds the resolved file name + path and a bounded
/// ring buffer of the last <see cref="MaxLines"/> raw lines. <see cref="Text"/> is a projection of
/// that buffer through the active quick <see cref="Filter"/> and <see cref="Search"/> box, rebuilt
/// once per <see cref="AppendBatch"/> and whenever the filter/search change. The owning
/// <see cref="MainViewModel"/> appends lines on the UI dispatcher; the Logs page binds the projection
/// into a read-only mono TextBox in every view mode.
/// </summary>
public sealed partial class LogPaneVm : ObservableObject
{
    private const int MaxLines = 500;

    /// <summary>Stable identity (script/rpt/adm/client) used by the resolver + clear/open ops.</summary>
    public string Key { get; }

    /// <summary>Display label shown in headers, the selector combo and tab headers.</summary>
    public string Title { get; }

    [ObservableProperty] private string _fileName = "(none)";
    [ObservableProperty] private string _text = "";

    /// <summary>Active quick filter bucket: "all" | "errors" | "warnings" | "connections" | "mods".</summary>
    [ObservableProperty] private string _filter = "all";

    /// <summary>Live search box text; case-insensitive substring, empty = no search.</summary>
    [ObservableProperty] private string _search = "";

    /// <summary>Pin the view to the tail as new lines arrive (toggle in the pane toolbar).</summary>
    [ObservableProperty] private bool _autoScroll = true;

    /// <summary>Lines currently shown after filter+search; pairs with <see cref="TotalCount"/> for the footer.</summary>
    [ObservableProperty] private int _visibleCount;

    /// <summary>Lines held in the buffer (unfiltered), capped at <see cref="MaxLines"/>.</summary>
    [ObservableProperty] private int _totalCount;

    // Bounded ring buffer of the last MaxLines raw lines. Text is a filtered projection rebuilt once per
    // AppendBatch (not per line) so a burst of thousands of lines is O(MaxLines) per flush, not O(N²).
    private readonly Queue<string> _lines = new();

    /// <summary>Accordion (List view) expand state; default expanded so the tail is visible.</summary>
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>True while this pane is popped out into its own window. The main Logs page greys the
    /// pane and shows a "in a separate window" placeholder; the detached window hosts a second view of
    /// this same VM (filter/search/auto-scroll stay in sync because both bind the same data).</summary>
    [ObservableProperty] private bool _isDetached;

    /// <summary>Resolved log file path (for Open-folder / Open-in-editor); null until the tail resolves it.</summary>
    public string? Path { get; set; }

    public LogPaneVm(string key, string title)
    {
        Key = key;
        Title = title;
    }

    /// <summary>Append a batch of raw lines (trim to the last <see cref="MaxLines"/>), then reproject
    /// <see cref="Text"/> once through the active filter/search. Call on the UI thread. Empty = no-op.</summary>
    public void AppendBatch(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        foreach (var l in lines) _lines.Enqueue(l);
        while (_lines.Count > MaxLines) _lines.Dequeue();
        Rebuild();
    }

    /// <summary>Clear the in-memory view (the underlying file is untouched).</summary>
    public void Clear()
    {
        _lines.Clear();
        Rebuild();
    }

    // Reproject the raw buffer into Text + counts. Re-runs when Filter/Search change (generated partials).
    partial void OnFilterChanged(string value) => Rebuild();
    partial void OnSearchChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var shown = _lines.Where(l => LogLineClassifier.Matches(l, Filter, Search)).ToList();
        TotalCount = _lines.Count;
        VisibleCount = shown.Count;
        Text = string.Join("\n", shown);
    }
}
