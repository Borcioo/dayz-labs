using System.Windows;
using Dzl.Core.Vcs;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Modal that gathers GitHub release options (tag, title, notes/auto-generate, pre-release, draft,
/// target branch, attach built PBOs). <see cref="Show"/> returns the chosen options + attach flag, or null
/// when cancelled.</summary>
public partial class ReleaseDialog : FluentWindow
{
    public ReleaseOptions? Options { get; private set; }
    public bool AttachBuiltAddons { get; private set; }

    private ReleaseDialog(string mod)
    {
        InitializeComponent();
        Title = TitleBarCtl.Title = $"Create release — {mod}";
    }

    /// <summary>Show the dialog; returns null if cancelled, otherwise the options + attach flag.</summary>
    public static (ReleaseOptions opts, bool attach)? Show(Window owner, string mod)
    {
        var dlg = new ReleaseDialog(mod) { Owner = owner };
        return dlg.ShowDialog() == true && dlg.Options is not null
            ? (dlg.Options, dlg.AttachBuiltAddons)
            : null;
    }

    private void OnGenerateToggled(object sender, RoutedEventArgs e)
    {
        if (NotesBox is not null) NotesBox.IsEnabled = GenerateNotes.IsChecked != true;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        var tag = TagBox.Text.Trim();
        if (tag.Length == 0) { StatusText.Text = "Tag is required (e.g. v1.0.0)."; return; }
        var gen = GenerateNotes.IsChecked == true;
        Options = new ReleaseOptions(
            Tag: tag,
            Title: TitleBox.Text.Trim() is { Length: > 0 } t ? t : null,
            Notes: gen ? null : NotesBox.Text,
            GenerateNotes: gen,
            Prerelease: Prerelease.IsChecked == true,
            Draft: Draft.IsChecked == true,
            Target: TargetBox.Text.Trim() is { Length: > 0 } tg ? tg : null);
        AttachBuiltAddons = AttachBuilt.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
