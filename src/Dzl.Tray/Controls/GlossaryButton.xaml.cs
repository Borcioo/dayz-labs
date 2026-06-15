using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>A small header button (book glyph + "Glossary") that opens a flyout listing this tab's
/// field cheat-sheet. Content is supplied by the host via <see cref="Entries"/> (a list of
/// <see cref="Help.GlossaryEntry"/>, usually from <see cref="Help.Glossary"/>) so the control stays generic.</summary>
public partial class GlossaryButton : UserControl
{
    public GlossaryButton() => InitializeComponent();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(GlossaryButton), new PropertyMetadata("Glossary"));

    /// <summary>Tab name shown in the flyout header (e.g. "Events" → "Events — field glossary").</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
        nameof(Entries), typeof(IEnumerable), typeof(GlossaryButton), new PropertyMetadata(null));

    /// <summary>The term/definition rows to show. Bound to a <see cref="Help.Glossary"/> list via x:Static.</summary>
    public IEnumerable? Entries
    {
        get => (IEnumerable?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    private void OnOpen(object sender, RoutedEventArgs e) => Flyout.IsOpen = true;
}
