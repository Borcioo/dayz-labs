using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray;

/// <summary>Modal "Add mod" dialog opened from the My Mods page: create a new mod, import an existing
/// source folder, or clone one from GitHub. Binds to the shared <see cref="MainViewModel"/> and calls
/// the same VM operations the page used to host inline. Closes itself on a successful add; the caller
/// refreshes the project list.</summary>
public partial class AddModWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    public AddModWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        NewModAuthorBox.Text = vm.CachedAuthor;
    }

    private bool _creatingMod;

    private async void OnCreateMod(object sender, RoutedEventArgs e)
    {
        if (_creatingMod) return;
        var name = NewModNameBox.Text.Trim();
        var author = NewModAuthorBox.Text.Trim();
        if (name.Length == 0) { NewModStatus.Text = "Enter a mod name."; return; }
        _creatingMod = true;
        NewModButton.IsEnabled = false;
        NewModStatus.Text = "creating…";
        try { NewModStatus.Text = await _vm.CreateModProjectAsync(name, author, NewModInitGit.IsChecked == true); }
        catch (Exception ex) { NewModStatus.Text = "✗ " + ex.Message; }
        finally { NewModButton.IsEnabled = true; _creatingMod = false; }
        if (NewModStatus.Text.StartsWith('✓')) Close();
    }

    private void OnImportMod(object sender, RoutedEventArgs e)
    {
        var path = ImportPathBox.Text.Trim();
        if (path.Length == 0) { ImportStatus.Text = "Pick a mod source folder."; return; }
        ImportStatus.Text = _vm.ImportModProject(path, ImportNameBox.Text, copy: ImportCopyChk.IsChecked == true);
        if (ImportStatus.Text.StartsWith('✓')) Close();
    }

    // Folder name follows the repo URL until the user types their own; emptying the box hands control
    // back to the auto-fill. _ghNameAutoFill guards the programmatic write from looking like user input.
    private bool _ghNameIsAuto = true;
    private bool _ghNameAutoFill;

    private void OnGhRepoChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ghNameIsAuto) return;
        _ghNameAutoFill = true;
        GhNameBox.Text = MainViewModel.SuggestModName(GhRepoBox.Text);
        _ghNameAutoFill = false;
    }

    private void OnGhNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_ghNameAutoFill) return;
        _ghNameIsAuto = GhNameBox.Text.Trim().Length == 0;
    }

    private bool _importingGitHub;

    private async void OnImportFromGitHub(object sender, RoutedEventArgs e)
    {
        if (_importingGitHub) return;
        var repo = GhRepoBox.Text.Trim();
        if (repo.Length == 0) { GhImportStatus.Text = "Enter a GitHub repo (owner/name or URL)."; return; }
        var mode = GhModeCombo.SelectedIndex switch
        {
            1 => MainViewModel.GitHubImportMode.Snapshot,
            2 => MainViewModel.GitHubImportMode.Fresh,
            _ => MainViewModel.GitHubImportMode.Clone,
        };
        _importingGitHub = true;
        GhImportButton.IsEnabled = false;
        GhImportStatus.Text = "cloning…";   // renders — the clone runs off the UI thread
        try { GhImportStatus.Text = await _vm.ImportFromGitHubAsync(repo, GhNameBox.Text, mode); }
        catch (Exception ex) { GhImportStatus.Text = "✗ " + ex.Message; }
        finally { GhImportButton.IsEnabled = true; _importingGitHub = false; }
        if (GhImportStatus.Text.StartsWith('✓')) Close();
    }

    /// <summary>Browse into a named TextBox. Tag form: "dir:&lt;FieldName&gt;" / "file:&lt;FieldName&gt;".</summary>
    private void OnBrowseInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return;
        var isFile = parts[0] == "file";
        var current = FindName(parts[1]) is TextBox cur ? cur.Text : "";
        var start = BrowseStartDir.Resolve(current, isFile,
            new[] { _vm.ProjectsRoot, _vm.Cfg.DayzPath }, Directory.Exists);
        var picked = isFile ? PickFile(start) : PickFolder(start);
        if (picked is null) return;
        if (FindName(parts[1]) is TextBox tb) tb.Text = picked;
    }

    private string? PickFolder(string? initialDir = null)
    {
        var dlg = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(initialDir)) dlg.InitialDirectory = initialDir;
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    private static string? PickFile(string? initialDir = null)
    {
        var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*" };
        if (!string.IsNullOrEmpty(initialDir)) dlg.InitialDirectory = initialDir;
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
