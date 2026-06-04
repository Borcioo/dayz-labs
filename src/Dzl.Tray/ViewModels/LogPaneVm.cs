using CommunityToolkit.Mvvm.ComponentModel;

namespace Dzl.Tray.ViewModels;

/// <summary>
/// One live log pane (script/rpt/adm/client). Holds the resolved file name + path and the
/// rolling tail text (capped to <see cref="MaxLines"/> so memory stays bounded). The owning
/// <see cref="MainViewModel"/> appends lines via <see cref="Append"/> on the UI dispatcher;
/// the Logs page binds <see cref="Text"/> into a read-only mono TextBox in every view mode.
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

    // Bounded ring buffer of the last MaxLines lines. Text is rebuilt once per AppendBatch (not per
    // line) so a burst of thousands of lines is O(MaxLines) per flush instead of O(N²) per line.
    private readonly Queue<string> _lines = new();

    /// <summary>Accordion (List view) expand state; default expanded so the tail is visible.</summary>
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Resolved log file path (for Open-folder); null until the tail resolves it.</summary>
    public string? Path { get; set; }

    public LogPaneVm(string key, string title)
    {
        Key = key;
        Title = title;
    }

    /// <summary>Append a batch of lines (trim to the last <see cref="MaxLines"/>), rebuilding
    /// <see cref="Text"/> once. Call on the UI thread. Empty batches are a no-op.</summary>
    public void AppendBatch(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        foreach (var l in lines) _lines.Enqueue(l);
        while (_lines.Count > MaxLines) _lines.Dequeue();
        Text = string.Join("\n", _lines);
    }

    /// <summary>Clear the in-memory view (the underlying file is untouched).</summary>
    public void Clear()
    {
        _lines.Clear();
        Text = "";
    }
}
