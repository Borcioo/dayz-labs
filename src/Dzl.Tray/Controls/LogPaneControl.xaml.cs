using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Dzl.Core.Logs;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Controls;

/// <summary>The body of a log pane: an AvalonEdit editor with gutter line numbers, token syntax colouring,
/// search-match highlighting and clickable stack-trace file references. Reacts to its bound
/// <see cref="LogPaneVm"/> (Text / Search / AutoScroll) and opens a clicked <c>file.c : line</c> in the
/// configured editor via the host <see cref="MainViewModel"/>.</summary>
public partial class LogPaneControl : UserControl
{
    private LogPaneVm? _vm;

    public LogPaneControl()
    {
        InitializeComponent();
        var tv = Editor.TextArea.TextView;
        tv.LineTransformers.Add(new SeverityTinter());                 // faint whole-line bg (runs first)
        tv.LineTransformers.Add(new TokenColorizer());                 // token foreground + underline
        tv.LineTransformers.Add(new SearchHighlighter(() => _vm?.Search ?? ""));  // match bg (wins, runs last)
        DataContextChanged += OnDataContextChanged;
        Editor.PreviewMouseMove += OnEditorMouseMove;
        Editor.PreviewMouseLeftButtonDown += OnEditorMouseDown;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as LogPaneVm;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        SetText();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPaneVm.Text)) SetText();
        else if (e.PropertyName == nameof(LogPaneVm.Search)) Editor.TextArea.TextView.Redraw();
    }

    /// <summary>Push the pane's projected text into the editor. Pinned to the tail when Auto-scroll is on;
    /// otherwise the vertical position is preserved so a reader isn't jerked around on each update.</summary>
    private void SetText()
    {
        var text = _vm?.Text ?? "";
        if (Editor.Text == text) return;
        var keepOffset = Editor.VerticalOffset;
        Editor.Text = text;
        if (_vm?.AutoScroll == true) Editor.ScrollToEnd();
        else Editor.ScrollToVerticalOffset(keepOffset);
    }

    // --- clickable file references -------------------------------------------------

    private (string Path, int Line)? RefAtMouse(MouseEventArgs e)
    {
        var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (pos is null) return null;
        var docLine = Editor.Document.GetLineByNumber(pos.Value.Line);
        var lineText = Editor.Document.GetText(docLine);
        return LogSyntax.FileRefAt(lineText, pos.Value.Column - 1);
    }

    private void OnEditorMouseMove(object sender, MouseEventArgs e) =>
        Editor.Cursor = RefAtMouse(e) is not null ? Cursors.Hand : null;

    private void OnEditorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (RefAtMouse(e) is not { } r) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel host && _vm is not null)
        {
            host.OpenLogFileRef(_vm, r.Path, r.Line);
            e.Handled = true;
        }
    }
}

/// <summary>Shared brushes for the log colourisers (tuned for the dark log surface). Frozen for reuse
/// across every visible line of every pane.</summary>
internal static class LogBrushes
{
    public static readonly Brush Timestamp = Frozen("#6B7280");
    public static readonly Brush Subsystem = Frozen("#5DCAA5");
    public static readonly Brush Error = Frozen("#F09595");
    public static readonly Brush Warning = Frozen("#EFB24A");
    public static readonly Brush FileRef = Frozen("#6AA9E9");
    public static readonly Brush Quoted = Frozen("#C9A26B");
    public static readonly Brush Pos = Frozen("#5DCAA5");
    public static readonly Brush KeyValue = Frozen("#8A8F98");
    public static readonly Brush Success = Frozen("#97C459");
    public static readonly Brush ErrorBg = Frozen("#22E24B4A");
    public static readonly Brush WarningBg = Frozen("#1FEF9F27");
    public static readonly Brush SearchBg = Frozen("#80E0A93B");
    public static readonly Brush SearchFg = Frozen("#1A1A1A");

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>Faint whole-line background for error/warning lines so they pop while scanning. Runs first so
/// the token colouriser (foreground) and search highlighter (background, wins) layer on top cleanly.</summary>
internal sealed class SeverityTinter : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);
        var cat = LogLineClassifier.Classify(text);
        Brush? bg = cat.HasFlag(LogCategory.Error) ? LogBrushes.ErrorBg
                  : cat.HasFlag(LogCategory.Warning) ? LogBrushes.WarningBg
                  : null;
        if (bg is not null)
            ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetBackgroundBrush(bg));
    }
}

/// <summary>Per-token foreground colouring from <see cref="LogSyntax.Tokenize"/> (timestamp, subsystem,
/// severity, file refs, quotes, …); file references are underlined to read as links.</summary>
internal sealed class TokenColorizer : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);
        foreach (var s in LogSyntax.Tokenize(text))
        {
            var brush = Brush(s.Token);
            if (brush is null) continue;
            var start = line.Offset + s.Start;
            ChangeLinePart(start, start + s.Length, el =>
            {
                el.TextRunProperties.SetForegroundBrush(brush);
                if (s.Token == LogToken.FileRef)
                    el.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
            });
        }
    }

    private static Brush? Brush(LogToken t) => t switch
    {
        LogToken.Timestamp => LogBrushes.Timestamp,
        LogToken.Subsystem => LogBrushes.Subsystem,
        LogToken.Error => LogBrushes.Error,
        LogToken.Warning => LogBrushes.Warning,
        LogToken.FileRef => LogBrushes.FileRef,
        LogToken.Quoted => LogBrushes.Quoted,
        LogToken.Pos => LogBrushes.Pos,
        LogToken.KeyValue => LogBrushes.KeyValue,
        LogToken.Success => LogBrushes.Success,
        _ => null,
    };
}

/// <summary>Highlights occurrences of the pane's search text (case-insensitive). Added last so its
/// background wins over token colours on a match.</summary>
internal sealed class SearchHighlighter : DocumentColorizingTransformer
{
    private readonly Func<string> _search;
    public SearchHighlighter(Func<string> search) => _search = search;

    protected override void ColorizeLine(DocumentLine line)
    {
        var q = _search();
        if (string.IsNullOrEmpty(q) || line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);
        var i = 0;
        while ((i = text.IndexOf(q, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = line.Offset + i;
            ChangeLinePart(start, start + q.Length, el =>
            {
                el.TextRunProperties.SetBackgroundBrush(LogBrushes.SearchBg);
                el.TextRunProperties.SetForegroundBrush(LogBrushes.SearchFg);
            });
            i += q.Length;
        }
    }
}
