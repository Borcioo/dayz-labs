using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dzl.Core.Vcs;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>A minimal per-mod git client: branch switch/create, stage which files to commit, commit message,
/// pull/push, and an "Open terminal" escape hatch for power users. Drives the CLI wrappers in
/// <see cref="Git"/> (which reuse the machine's git/gh auth). Code-behind — small, self-contained.</summary>
public partial class GitWindow : FluentWindow
{
    private readonly string _dir;
    private readonly string _name;
    private readonly MainViewModel _vm;

    // Re-entrancy guard: every stage toggle / commit / pull / push re-runs the refresh, and each
    // refresh fans out five git process spawns. The guard disables the files list while it runs.
    private bool _busy;

    public GitWindow(MainViewModel vm, string name, string dir)
    {
        _vm = vm;
        _name = name;
        _dir = dir;
        InitializeComponent();
        Title = TitleBarCtl.Title = $"Git — {name}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    /// <summary>One refresh snapshot: all five git reads (repo/branches/status/remote/changed files) gathered
    /// off the UI thread in a single <see cref="Task.Run"/>, then applied to the controls on the UI thread.</summary>
    private sealed record GitSnapshot(
        bool Repo,
        string? Current,
        IReadOnlyList<string> Branches,
        RepoStatus? Status,
        bool HasRemote,
        IReadOnlyList<GitFileRow> Rows);

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        FilesList.IsEnabled = false;
        try
        {
            var dir = _dir;
            var snap = await Task.Run(() =>
            {
                if (!Git.IsRepo(dir))
                    return new GitSnapshot(false, null, Array.Empty<string>(), null, false, Array.Empty<GitFileRow>());
                var (current, all) = Git.Branches(dir);
                var s = Git.Status(dir);
                var hasRemote = Git.RemoteUrl(dir) is not null;   // an 'origin' remote exists at all
                var rows = Git.ChangedFiles(dir).Select(f => new GitFileRow(f)).ToList();
                return new GitSnapshot(true, current, all, s, hasRemote, rows);
            });

            InitBtn.Visibility = snap.Repo ? Visibility.Collapsed : Visibility.Visible;
            if (!snap.Repo)
            {
                BranchCombo.ItemsSource = null;
                FilesList.ItemsSource = null;
                EmptyHint.Visibility = Visibility.Collapsed;
                AheadBehind.Text = "";
                StatusText.Text = "not a git repo — click Init repo";
                return;
            }

            BranchCombo.ItemsSource = snap.Branches;
            BranchCombo.SelectedItem = snap.Current;

            var s = snap.Status!;   // non-null whenever Repo is true (set in the snapshot above)
            AheadBehind.Text = !snap.HasRemote ? "· no remote (Publish to create one)"
                : (s.Ahead > 0 || s.Behind > 0) ? $"↑{s.Ahead} ↓{s.Behind}" : "· up to date";
            // No remote yet → offer Publish (creates the GitHub repo); otherwise Push.
            PushBtn.Visibility = snap.HasRemote ? Visibility.Visible : Visibility.Collapsed;
            PublishBtn.Visibility = snap.HasRemote ? Visibility.Collapsed : Visibility.Visible;
            PullBtn.IsEnabled = snap.HasRemote;
            ReleaseBtn.Visibility = snap.HasRemote ? Visibility.Visible : Visibility.Collapsed;

            FilesList.ItemsSource = snap.Rows;
            EmptyHint.Visibility = snap.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _busy = false; FilesList.IsEnabled = true; }
    }

    private void Report((bool ok, string msg) r) => StatusText.Text = (r.ok ? "✓ " : "✗ ") + FirstLine(r.msg);
    private static string FirstLine(string s) => s.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";

    private async void OnToggleStage(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: GitFileRow row } cb) return;
        Report(cb.IsChecked == true ? Git.Stage(_dir, row.Path) : Git.Unstage(_dir, row.Path));
        await RefreshAsync();
    }

    private async void OnStageAll(object sender, RoutedEventArgs e) { Report(Git.StageAll(_dir)); await RefreshAsync(); }

    private async void OnCheckout(object sender, RoutedEventArgs e)
    {
        if (BranchCombo.SelectedItem is not string b || b.Length == 0) return;
        Report(Git.Checkout(_dir, b));
        await RefreshAsync();
    }

    private async void OnNewBranch(object sender, RoutedEventArgs e)
    {
        var name = PromptDialog.Show(this, "New branch", "Branch name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        Report(Git.CreateBranch(_dir, name.Trim()));
        await RefreshAsync();
    }

    private async void OnCommit(object sender, RoutedEventArgs e)
    {
        var msg = CommitMsg.Text.Trim();
        if (msg.Length == 0) { StatusText.Text = "enter a commit message"; return; }
        var r = Git.CommitStaged(_dir, msg);
        Report(r);
        if (r.ok) CommitMsg.Text = "";
        await RefreshAsync();
    }

    private async void OnPull(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "pulling…";
        Report(await Task.Run(() => Git.Pull(_dir)));
        await RefreshAsync();
    }

    private async void OnPush(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "pushing…";
        PushBtn.IsEnabled = false;
        try { Report(await Task.Run(() => Git.Push(_dir))); }
        finally { PushBtn.IsEnabled = true; }
        await RefreshAsync();
    }

    private async void OnPublish(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "publishing to GitHub…";
        PublishBtn.IsEnabled = false;
        try { Report(await _vm.PublishForGitAsync(_name)); }
        finally { PublishBtn.IsEnabled = true; }
        await RefreshAsync();
    }

    private async void OnRelease(object sender, RoutedEventArgs e)
    {
        var choice = ReleaseDialog.Show(this, _name);
        if (choice is not { } c) return;
        StatusText.Text = "creating release…";
        ReleaseBtn.IsEnabled = false;
        try { Report(await _vm.ReleaseForGitAsync(_name, c.opts, c.attach)); }
        finally { ReleaseBtn.IsEnabled = true; }
        await RefreshAsync();
    }

    private async void OnInit(object sender, RoutedEventArgs e)
    {
        var r = Git.Init(_dir);
        StatusText.Text = (r.ok ? "✓ " : "✗ ") + FirstLine(r.msg);
        await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnOpenTerminal(object sender, RoutedEventArgs e)
    {
        if (!ShellOpen.Terminal(_dir)) StatusText.Text = "✗ couldn't open a terminal";
    }
}

/// <summary>A changed-file row in the git window: path + status + staged flag, with status-pill colors.</summary>
public sealed class GitFileRow
{
    public string Path { get; }
    public string Status { get; }
    public bool IsStaged { get; set; }
    public Brush StatusBg { get; }
    public Brush StatusFg { get; }

    public GitFileRow(Git.ChangedFile f)
    {
        Path = f.Path;
        Status = f.Status;
        IsStaged = f.Staged;
        var (bg, fg) = f.Conflicted ? ("#5A1F1F", "#FF6B6B")
            : f.Untracked ? ("#3A3A3A", "#BBBBBB")
            : f.Staged ? ("#1F4D33", "#7CE3A1")
            : ("#4D3A1A", "#F0C04A");
        StatusBg = BrushUtil.Freeze(bg);
        StatusFg = BrushUtil.Freeze(fg);
    }
}
