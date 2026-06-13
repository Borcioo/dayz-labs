using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray.Views;

/// <summary>My Mods page (source projects): create / import / clone-from-GitHub a mod, then per
/// project Build / Git / open-on-GitHub / open-folder / link / unlink / delete. All state lives on
/// <see cref="MainViewModel"/> (the inherited DataContext).</summary>
public partial class MyModsView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public MyModsView() => InitializeComponent();

    /// <summary>Refresh the project list and seed the author box on first show. Called by the host
    /// window when the My Mods page becomes visible.</summary>
    public void RefreshOnShow()
    {
        if (Vm is null) return;
        Vm.RefreshModProjects();
        if (NewModAuthorBox.Text.Length == 0) NewModAuthorBox.Text = Vm.CachedAuthor;
    }

    // Re-entrancy guard for the create-mod flow (the button is disabled while it runs, but a fast
    // double-tap before the first frame renders could still re-enter — bool gate is belt-and-braces).
    private bool _creatingMod;

    private async void OnCreateMod(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _creatingMod) return;
        var name = NewModNameBox.Text.Trim();
        var author = NewModAuthorBox.Text.Trim();
        if (name.Length == 0) { NewModStatus.Text = "Enter a mod name."; return; }
        _creatingMod = true;
        NewModButton.IsEnabled = false;
        NewModStatus.Text = "creating…";
        try { NewModStatus.Text = await Vm.CreateModProjectAsync(name, author, NewModInitGit.IsChecked == true); }
        catch (Exception ex) { NewModStatus.Text = "✗ " + ex.Message; }
        finally { NewModButton.IsEnabled = true; _creatingMod = false; }
        if (NewModStatus.Text.StartsWith('✓')) NewModNameBox.Text = "";
    }

    // Open the project's git remote in the browser (button disabled when there's no remote).
    private void OnOpenRepoUrl(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    // Open the per-mod git client window.
    private void OnOpenGit(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string name }) return;
        // No Owner: an owned WPF-UI FluentWindow (Mica) can minimize/hide its owner when closed. As an
        // independent top-level tool window, closing it can't touch the main window.
        new GitWindow(Vm, name, Vm.ModDirOf(name)).Show();
    }

    // Open the per-mod build console (preflight + build log). Ownerless like GitWindow.
    private void OnOpenBuildWindow(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string name }) return;
        new BuildWindow(Vm, name).Show();
    }

    // Build opens the Build console window — one build flow (preflight findings, options, live
    // log, diagnostics). Signing keys are managed in Settings → Signing.
    private void OnBuildMod(object sender, RoutedEventArgs e) => OnOpenBuildWindow(sender, e);

    // Folder name follows the repo URL until the user types their own; emptying the box hands
    // control back to the auto-fill. _ghNameAutoFill guards the programmatic write from being
    // mistaken for user input.
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
        if (Vm is null || _importingGitHub) return;
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
        GhImportStatus.Text = "cloning…";   // now renders — the clone runs off the UI thread
        try { GhImportStatus.Text = await Vm.ImportFromGitHubAsync(repo, GhNameBox.Text, mode); }
        catch (Exception ex) { GhImportStatus.Text = "✗ " + ex.Message; }
        finally { GhImportButton.IsEnabled = true; _importingGitHub = false; }
        if (GhImportStatus.Text.StartsWith('✓')) { GhRepoBox.Text = ""; GhNameBox.Text = ""; }
    }

    private void OnImportMod(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var path = ImportPathBox.Text.Trim();
        if (path.Length == 0) { ImportStatus.Text = "Pick a mod source folder."; return; }
        ImportStatus.Text = Vm.ImportModProject(path, ImportNameBox.Text);
        if (ImportStatus.Text.StartsWith('✓')) { ImportPathBox.Text = ""; ImportNameBox.Text = ""; }
    }

    private void OnQuickJunction(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement { Tag: string name })
            NewModStatus.Text = Vm.QuickJunction(name);
    }

    // Open the per-module settings modal (⚙). Shared with the Mods page; the host keeps the global
    // Settings page in sync afterwards in case the module edits config it mirrors.
    private void OnModuleSettings(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string module }) return;
        var owner = Window.GetWindow(this);
        new ModuleSettingsWindow(Vm, module) { Owner = owner }.ShowDialog();
        (owner as MainWindow)?.SyncSettingsPage();
    }

    private void OnOpenModFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}\n\n(missing, or the P: link is broken — try Rescan / re-link)",
                "Open mod folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string folder }) return;
        var msg = Vm.OpenInEditor(folder);
        if (msg.StartsWith('✗'))
            System.Windows.MessageBox.Show(msg.TrimStart('✗', ' '), "Open in editor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnUnlinkMod(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement { Tag: string name })
            NewModStatus.Text = Vm.UnlinkMod(name);
    }

    private bool _deletingProject;

    // Delete a mod project (destructive). Yes = source + build, No = source only, Cancel = abort.
    private async void OnDeleteProject(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _deletingProject) return;
        if (sender is not FrameworkElement { Tag: string name }) return;
        var r = System.Windows.MessageBox.Show(
            $"Delete project \"{name}\"?\n\nThis removes its P: link and deletes the source folder — this can't be undone.\n\n" +
            "Yes  → also delete the built @" + name + " output\n" +
            "No   → delete the source only\n" +
            "Cancel → keep everything",
            "Delete project", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Cancel) return;
        _deletingProject = true;
        NewModStatus.Text = "deleting…";
        try { NewModStatus.Text = await Vm.DeleteModProjectAsync(name, alsoBuild: r == System.Windows.MessageBoxResult.Yes); }
        catch (Exception ex) { NewModStatus.Text = "✗ " + ex.Message; }
        finally { _deletingProject = false; }
    }

    /// <summary>Browse into a named TextBox on this page. Tag form: "dir:&lt;FieldName&gt;".</summary>
    private void OnBrowseInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return;
        var picked = parts[0] == "file" ? PickFile() : PickFolder();
        if (picked is null) return;
        if (FindName(parts[1]) is TextBox tb) tb.Text = picked;
        else if (FindName(parts[1]) is System.Windows.Controls.ComboBox cb) cb.Text = picked;
    }

    private string? PickFolder()
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FolderName : null;
    }

    private static string? PickFile()
    {
        var dlg = new OpenFileDialog { Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
