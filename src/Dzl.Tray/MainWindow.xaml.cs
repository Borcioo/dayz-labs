using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>
/// The launcher main window: a Wpf.Ui <see cref="FluentWindow"/> with a title bar, a
/// persistent top action bar (mode toggle, profile switcher, server/client status pills)
/// and a left ListBox-based nav rail that swaps between five content panels
/// (Dashboard, Mods, Logs, Tools, Settings). All five panels are fully built; the Logs,
/// Tools and Settings pages own their interaction logic here (auto-scroll, file/folder
/// pickers, background tool runs and inline config/params editing — no modal dialogs).
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.ConfigPath());
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();

        // Select Dashboard on load so a panel is always visible; selecting the first
        // NavTop item raises OnNavChanged, which calls ShowPage("dashboard").
        Loaded += (_, _) => NavGeneral.SelectedIndex = 0;   // Dashboard
    }

    /// <summary>Every nav rail (grouped sections + pinned system group). Selection is single across all of
    /// them — selecting in one clears the others.</summary>
    private ListBox[] NavRails => new[] { NavGeneral, NavServer, NavBottom };

    // --- Navigation: swap the visible content panel based on the selected rail item ---

    /// <summary>True while we are programmatically restoring the rail selection (after the
    /// "Setup" pseudo-item opened the wizard) so the resulting SelectionChanged is ignored.</summary>
    private bool _restoringNav;

    /// <summary>Tag of the last real content page shown, so "Setup" (a button-like item) can
    /// restore the rail to it instead of becoming a sticky selection.</summary>
    private string _currentPageTag = "dashboard";

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringNav) return;
        if (sender is ListBox lb && lb.SelectedItem is ListBoxItem { Tag: string tag })
        {
            // "Setup" is not a page: open the wizard, then bounce the selection back.
            if (tag == "setup")
            {
                OpenSetupWizard();
                RestoreNavToCurrentPage();
                return;
            }

            // "Economy" opens its own modeless window (more room; works alongside the main
            // window) — like Setup, it's not an in-app page, so bounce the rail selection back.
            if (tag == "economy")
            {
                OpenEconomyWindow();
                RestoreNavToCurrentPage();
                return;
            }

            // Clear every other rail's selection so only one item looks active.
            foreach (var rail in NavRails)
                if (!ReferenceEquals(rail, lb)) rail.SelectedItem = null;
            _currentPageTag = tag;
            ShowPage(tag);
        }
    }

    /// <summary>Reselect the rail item for <see cref="_currentPageTag"/> without re-running page
    /// logic (guarded so the programmatic SelectionChanged is a no-op).</summary>
    private void RestoreNavToCurrentPage()
    {
        _restoringNav = true;
        try
        {
            foreach (var rail in NavRails) rail.SelectedItem = null;
            foreach (var rail in NavRails)
                if (TrySelect(rail, _currentPageTag)) break;
        }
        finally { _restoringNav = false; }
    }

    private static bool TrySelect(ListBox rail, string tag)
    {
        foreach (var obj in rail.Items)
            if (obj is ListBoxItem { Tag: string t } item && t == tag)
            {
                rail.SelectedItem = item;
                return true;
            }
        return false;
    }

    private void ShowPage(string tag)
    {
        if (PageDashboard is null) return; // not yet templated
        PageDashboard.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PageMods.Visibility = tag == "mods" ? Visibility.Visible : Visibility.Collapsed;
        PageMyMods.Visibility = tag == "mymods" ? Visibility.Visible : Visibility.Collapsed;
        PageServers.Visibility = tag == "servers" ? Visibility.Visible : Visibility.Collapsed;
        PageBases.Visibility = tag == "bases" ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PageMcp.Visibility = tag == "mcp" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;

        // Refresh page-local state on show.
        if (tag == "tools") PageTools.RefreshToolsPage();
        if (tag == "mymods") PageMyMods.RefreshOnShow();
        if (tag == "servers") { _vm.RefreshServers(); _vm.RefreshBases(); }   // base dropdown needs bases
        if (tag == "bases") _vm.RefreshBases();
        if (tag == "settings") { PageSettings.Reload(); _ = _vm.RefreshGitHubAuthAsync(); _vm.RefreshSteamAccount(); }
    }

    // --- Economy window (modeless, single instance) ------------------------

    private EconomyWindow? _economyWin;

    /// <summary>Open (or focus) the Central Economy editor window. Modeless and ownerless on
    /// purpose: an owned Mica FluentWindow hides its owner when closed, and the editor must not
    /// block the main window. The shared MainViewModel keeps all editor state across closes.</summary>
    private void OpenEconomyWindow()
    {
        if (_economyWin is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return;
        }
        _economyWin = new EconomyWindow(_vm);
        _economyWin.Closed += (_, _) => _economyWin = null;
        _economyWin.Show();
    }

    // --- Top action bar handlers ------------------------------------------

    private void OnModeToggleClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ToggleModeCommand.CanExecute(null))
            _vm.ToggleModeCommand.Execute(null);
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SwitchPresetCommand.CanExecute(null))
            _vm.SwitchPresetCommand.Execute(null);
    }

    // === Work drive (bottom status bar) ===================================

    // The Tools + Settings pages own their own copies (ToolsView / SettingsView); this stays for
    // the app-wide bottom status bar's "Mount P:" quick button.
    private void OnMountWorkDrive(object sender, RoutedEventArgs e) => _vm.MountWorkDrive();

    // === Setup wizard =====================================================

    /// <summary>Open the Setup Wizard modally; on Finish, reload the VM so the new config/profile
    /// takes effect immediately, and re-read the Settings page. Shared by the Settings page's
    /// "Run setup wizard…" button (SettingsView) and the "Setup" nav-rail item.</summary>
    internal void OpenSetupWizard()
    {
        var wizard = new SetupWizardWindow(App.ConfigPath()) { Owner = this };
        if (wizard.ShowDialog() == true)
        {
            _vm.Reload();
            PageSettings.Reload();
        }
    }

    /// <summary>Re-read the global Settings page from the live config. Called by the Mods / My Mods
    /// views after the per-module settings modal closes, so the Settings page mirrors any config
    /// the module edited (the pages are never visible at once, but this keeps state consistent).</summary>
    /// <summary>Programmatically navigate to a nav tag (used by the screenshot smoke) — selects the
    /// rail item so the normal OnNavChanged flow runs (shows the page, or opens Economy/Setup).</summary>
    public void NavigateTo(string tag)
    {
        foreach (var rail in NavRails)
            foreach (var obj in rail.Items)
                if (obj is ListBoxItem { Tag: string t } item && t == tag)
                {
                    rail.SelectedItem = item;
                    return;
                }
    }

    internal void SyncSettingsPage() => PageSettings.Reload();

    // (The Servers / My Mods / Settings pages now live in Views/ServersView, MyModsView and
    //  SettingsView; per-server settings + the launch-params editor live in ServerEditorWindow.)
}
