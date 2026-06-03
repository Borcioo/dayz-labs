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

    /// <summary>Accordion (List view) expand state; default expanded so the tail is visible.</summary>
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Resolved log file path (for Open-folder); null until the tail resolves it.</summary>
    public string? Path { get; set; }

    public LogPaneVm(string key, string title)
    {
        Key = key;
        Title = title;
    }

    /// <summary>Append one line, trimming to the last <see cref="MaxLines"/> lines.</summary>
    public void Append(string line)
    {
        var text = Text.Length == 0 ? line : Text + "\n" + line;
        var nl = CountLines(text);
        if (nl > MaxLines)
        {
            var idx = 0;
            var drop = nl - MaxLines;
            for (int i = 0; i < drop; i++)
            {
                idx = text.IndexOf('\n', idx);
                if (idx < 0) break;
                idx++;
            }
            if (idx > 0) text = text[idx..];
        }
        Text = text;
    }

    /// <summary>Clear the in-memory view (the underlying file is untouched).</summary>
    public void Clear() => Text = "";

    private static int CountLines(string s)
    {
        if (s.Length == 0) return 0;
        var n = 1;
        foreach (var c in s) if (c == '\n') n++;
        return n;
    }
}
