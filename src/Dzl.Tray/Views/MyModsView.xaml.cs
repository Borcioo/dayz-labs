using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>My Mods page (source projects): create / import / clone-from-GitHub a mod, then per
/// project Build / Git / open-on-GitHub / open-folder / link / unlink / delete. All state lives on
/// <see cref="MainViewModel"/> (the inherited DataContext).</summary>
public partial class MyModsView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public MyModsView() => InitializeComponent();

    /// <summary>Refresh the project list. Called by the host window when the My Mods page becomes
    /// visible.</summary>
    public void RefreshOnShow() => Vm?.RefreshModProjects();

    // Open the "Add mod" modal (new / import folder / clone from GitHub), then pick up whatever it
    // created. The dialog closes itself on success.
    private void OnAddMod(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        new AddModWindow(Vm) { Owner = Window.GetWindow(this) }.ShowDialog();
        Vm.RefreshModProjects();
    }

    private void ShowRowStatus(string text)
    {
        RowStatus.Text = text;
        RowStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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

    private void OnBuildPack(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement { DataContext: ModProjectVm pack } && pack.IsPack)
            new PackBuildWindow(Vm, pack).Show();
    }

    private void OnQuickJunction(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement { Tag: string name })
            ShowRowStatus(Vm.QuickJunction(name));
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
            ShowRowStatus(Vm.UnlinkMod(name));
    }

    private bool _deletingProject;

    // Delete a mod project (destructive). Yes = also build output, No = project only, Cancel = abort. A
    // link-imported project only ever loses its junction — its external source is never touched.
    private async void OnDeleteProject(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _deletingProject) return;
        if (sender is not FrameworkElement { DataContext: ModProjectVm proj }) return;
        var name = proj.Name;
        var kind = proj.IsPack ? "pack" : "project";
        var r = proj.IsImportLink
            ? System.Windows.MessageBox.Show(
                $"Remove {kind} \"{name}\" from projects?\n\nIt was imported as a LINK, so this only removes the " +
                "junction — the original source folder is left completely untouched.\n\n" +
                $"Yes  → also delete the built @{name} output\n" +
                "No   → remove the link only\n" +
                "Cancel → keep everything",
                $"Remove {kind}", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning)
            : System.Windows.MessageBox.Show(
                $"Delete {kind} \"{name}\"?\n\nThis removes its P: link and deletes the source folder — this can't be undone.\n\n" +
                $"Yes  → also delete the built @{name} output\n" +
                "No   → delete the source only\n" +
                "Cancel → keep everything",
                $"Delete {kind}", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Cancel) return;
        _deletingProject = true;
        ShowRowStatus("working…");
        try { ShowRowStatus(await Vm.DeleteModProjectAsync(name, alsoBuild: r == System.Windows.MessageBoxResult.Yes)); }
        catch (Exception ex) { ShowRowStatus("✗ " + ex.Message); }
        finally { _deletingProject = false; }
    }
}
