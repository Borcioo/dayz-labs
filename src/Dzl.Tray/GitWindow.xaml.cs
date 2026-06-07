using System.Linq;
using System.Threading.Tasks;
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

    public GitWindow(MainViewModel vm, string name, string dir)
    {
        _vm = vm;
        _name = name;
        _dir = dir;
        InitializeComponent();
        Title = TitleBarCtl.Title = $"Git — {name}";
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var repo = Git.IsRepo(_dir);
        InitBtn.Visibility = repo ? Visibility.Collapsed : Visibility.Visible;
        if (!repo)
        {
            BranchCombo.ItemsSource = null;
            FilesList.ItemsSource = null;
            EmptyHint.Visibility = Visibility.Collapsed;
            AheadBehind.Text = "";
            StatusText.Text = "not a git repo — click Init repo";
            return;
        }

        var (current, all) = Git.Branches(_dir);
        BranchCombo.ItemsSource = all;
        BranchCombo.SelectedItem = current;

        var s = Git.Status(_dir);
        var hasRemote = Git.RemoteUrl(_dir) is not null;   // an 'origin' remote exists at all
        AheadBehind.Text = !hasRemote ? "· no remote (Publish to create one)"
            : (s.Ahead > 0 || s.Behind > 0) ? $"↑{s.Ahead} ↓{s.Behind}" : "· up to date";
        // No remote yet → offer Publish (creates the GitHub repo); otherwise Push.
        PushBtn.Visibility = hasRemote ? Visibility.Visible : Visibility.Collapsed;
        PublishBtn.Visibility = hasRemote ? Visibility.Collapsed : Visibility.Visible;
        PullBtn.IsEnabled = hasRemote;

        var rows = Git.ChangedFiles(_dir).Select(f => new GitFileRow(f)).ToList();
        FilesList.ItemsSource = rows;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Report((bool ok, string msg) r) => StatusText.Text = (r.ok ? "✓ " : "✗ ") + FirstLine(r.msg);
    private static string FirstLine(string s) => s.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";

    private void OnToggleStage(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: GitFileRow row } cb) return;
        Report(cb.IsChecked == true ? Git.Stage(_dir, row.Path) : Git.Unstage(_dir, row.Path));
        Refresh();
    }

    private void OnStageAll(object sender, RoutedEventArgs e) { Report(Git.StageAll(_dir)); Refresh(); }

    private void OnCheckout(object sender, RoutedEventArgs e)
    {
        if (BranchCombo.SelectedItem is not string b || b.Length == 0) return;
        Report(Git.Checkout(_dir, b));
        Refresh();
    }

    private void OnNewBranch(object sender, RoutedEventArgs e)
    {
        var name = PromptDialog.Show(this, "New branch", "Branch name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        Report(Git.CreateBranch(_dir, name.Trim()));
        Refresh();
    }

    private void OnCommit(object sender, RoutedEventArgs e)
    {
        var msg = CommitMsg.Text.Trim();
        if (msg.Length == 0) { StatusText.Text = "enter a commit message"; return; }
        var r = Git.CommitStaged(_dir, msg);
        Report(r);
        if (r.ok) CommitMsg.Text = "";
        Refresh();
    }

    private async void OnPull(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "pulling…";
        Report(await Task.Run(() => Git.Pull(_dir)));
        Refresh();
    }

    private async void OnPush(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "pushing…";
        PushBtn.IsEnabled = false;
        try { Report(await Task.Run(() => Git.Push(_dir))); }
        finally { PushBtn.IsEnabled = true; }
        Refresh();
    }

    private async void OnPublish(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "publishing to GitHub…";
        PublishBtn.IsEnabled = false;
        try { Report(await _vm.PublishForGitAsync(_name)); }
        finally { PublishBtn.IsEnabled = true; }
        Refresh();
    }

    private void OnInit(object sender, RoutedEventArgs e)
    {
        var r = Git.Init(_dir);
        StatusText.Text = (r.ok ? "✓ " : "✗ ") + FirstLine(r.msg);
        Refresh();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

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
        StatusBg = Freeze(bg);
        StatusFg = Freeze(fg);
    }

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
