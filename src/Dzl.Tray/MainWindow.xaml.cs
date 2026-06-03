using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Config;
using Dzl.Core.Tools;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

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
        Loaded += (_, _) => NavTop.SelectedIndex = 0;
    }

    // --- Navigation: swap the visible content panel based on the selected rail item ---

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is ListBoxItem { Tag: string tag })
        {
            // Clear the other rail's selection so only one item looks active.
            if (ReferenceEquals(lb, NavTop)) NavBottom.SelectedItem = null;
            else NavTop.SelectedItem = null;
            ShowPage(tag);
        }
    }

    private void ShowPage(string tag)
    {
        if (PageDashboard is null) return; // not yet templated
        PageDashboard.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        PageMods.Visibility = tag == "mods" ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Refresh page-local state on show.
        if (tag == "tools") RefreshToolsPage();
        if (tag == "settings") { LoadSettingsFields(); LoadParamsEditor(); }
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

    // === LOGS page ========================================================

    /// <summary>Auto-scroll a log pane to the end whenever new lines arrive. Wired from the
    /// log TextBox inside <c>LogPaneTemplate</c>, so it works in every view mode.</summary>
    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    // Open-folder and clear are now VM commands (OpenLogFolderCommand/ClearLogCommand) bound
    // directly from LogPaneTemplate with the pane as CommandParameter.

    // === TOOLS page =======================================================

    private void RefreshToolsPage()
    {
        _vm.RefreshTools();
        PackToolMissing.Visibility = _vm.ToolExe("addonbuilder") is null ? Visibility.Visible : Visibility.Collapsed;
        PackButton.IsEnabled = _vm.ToolExe("addonbuilder") is not null;
        PaaToolMissing.Visibility = _vm.ToolExe("imagetopaa") is null ? Visibility.Visible : Visibility.Collapsed;
        PaaButton.IsEnabled = _vm.ToolExe("imagetopaa") is not null;
        UnbinToolMissing.Visibility = _vm.ToolExe("cfgconvert") is null ? Visibility.Visible : Visibility.Collapsed;
        UnbinButton.IsEnabled = _vm.ToolExe("cfgconvert") is not null;
    }

    private void OnRefreshTools(object sender, RoutedEventArgs e) => RefreshToolsPage();
    private void OnRefreshWorkDrive(object sender, RoutedEventArgs e) => _vm.RefreshWorkDrive();

    private void OnLaunchTool(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToolEntry t }) _vm.LaunchTool(t);
    }

    private void OnMountWorkDrive(object sender, RoutedEventArgs e) => _vm.MountWorkDrive();
    private void OnUnmountWorkDrive(object sender, RoutedEventArgs e) => _vm.UnmountWorkDrive();

    private async void OnPackPbo(object sender, RoutedEventArgs e)
    {
        var exe = _vm.ToolExe("addonbuilder");
        if (exe is null) { PackOutput.Text = "Addon Builder not found."; return; }
        var src = PackSrcBox.Text.Trim();
        var dst = PackDstBox.Text.Trim();
        if (src.Length == 0 || dst.Length == 0) { PackOutput.Text = "Pick a source and output folder."; return; }

        PackButton.IsEnabled = false;
        PackOutput.Text = "Packing…";
        try
        {
            var r = await _vm.PackAsync(exe, src, dst, PackPrefixBox.Text, PackSignBox.Text);
            PackOutput.Text = $"{(r.Ok ? "OK" : $"FAILED (exit {r.ExitCode})")}\n{r.Output}";
        }
        catch (Exception ex) { PackOutput.Text = "Error: " + ex.Message; }
        finally { PackButton.IsEnabled = true; PackOutput.ScrollToEnd(); }
    }

    private async void OnConvertPaa(object sender, RoutedEventArgs e)
    {
        var exe = _vm.ToolExe("imagetopaa");
        if (exe is null) { PaaOutput.Text = "ImageToPAA not found."; return; }
        var dir = PaaDirBox.Text.Trim();
        if (dir.Length == 0) { PaaOutput.Text = "Pick an image folder."; return; }
        var recursive = PaaRecursive.IsChecked == true;

        // First surface suffix warnings from the plan.
        var plan = _vm.PlanPaa(dir, recursive);
        if (plan.Count == 0) { PaaOutput.Text = "No .png/.tga files found."; return; }
        var warnings = plan.Where(j => !j.SuffixOk).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{plan.Count} file(s) to convert.");
        if (warnings.Count > 0)
        {
            sb.AppendLine($"⚠ {warnings.Count} file(s) lack a known texture suffix (_co/_nohq/…):");
            foreach (var w in warnings) sb.AppendLine("  " + Path.GetFileName(w.Input));
        }
        sb.AppendLine("Converting…");
        PaaOutput.Text = sb.ToString();

        PaaButton.IsEnabled = false;
        var ok = 0; var fail = 0;
        var progress = new Progress<PaaResult>(r =>
        {
            if (r.Ok) ok++; else fail++;
            PaaOutput.AppendText($"{(r.Ok ? "  ok " : "  ✗  ")}{Path.GetFileName(r.Input)} — {r.Message}\n");
            PaaOutput.ScrollToEnd();
        });
        try
        {
            await _vm.ConvertPaaAsync(exe, dir, recursive, progress);
            PaaOutput.AppendText($"Done. {ok} ok, {fail} failed.\n");
        }
        catch (Exception ex) { PaaOutput.AppendText("Error: " + ex.Message + "\n"); }
        finally { PaaButton.IsEnabled = true; PaaOutput.ScrollToEnd(); }
    }

    private async void OnUnbinarize(object sender, RoutedEventArgs e)
    {
        var exe = _vm.ToolExe("cfgconvert");
        if (exe is null) { UnbinOutput.Text = "CfgConvert not found."; return; }
        var bin = BinFileBox.Text.Trim();
        if (bin.Length == 0) { UnbinOutput.Text = "Pick a .bin file."; return; }
        var outCpp = Path.ChangeExtension(bin, ".cpp");

        UnbinButton.IsEnabled = false;
        UnbinOutput.Text = "Unbinarizing…";
        try
        {
            var (ok, output) = await _vm.UnbinarizeAsync(exe, bin, outCpp);
            UnbinOutput.Text = $"{(ok ? "OK → " + outCpp : "FAILED")}\n{output}";
        }
        catch (Exception ex) { UnbinOutput.Text = "Error: " + ex.Message; }
        finally { UnbinButton.IsEnabled = true; UnbinOutput.ScrollToEnd(); }
    }

    // Folder picker → set the target TextBox text (Tag picks which one).
    private void OnBrowseFolderInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string which }) return;
        var dir = PickFolder();
        if (dir is null) return;
        switch (which)
        {
            case "packsrc": PackSrcBox.Text = dir; break;
            case "packdst": PackDstBox.Text = dir; break;
            case "paadir": PaaDirBox.Text = dir; break;
        }
    }

    private void OnBrowseBinFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Binarized config (*.bin)|*.bin|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true) BinFileBox.Text = dlg.FileName;
    }

    // === SETTINGS page ====================================================

    private void LoadSettingsFields()
    {
        var c = _vm.Cfg;
        CfgDayzPath.Text = c.DayzPath;
        CfgDayzToolsPath.Text = c.DayzToolsPath;
        CfgProfilesPath.Text = c.ProfilesPath;
        CfgClientProfilesPath.Text = c.ClientProfilesPath;
        CfgExeDebug.Text = c.ExeDebug;
        CfgExeNormal.Text = c.ExeNormal;
        CfgClientExeDebug.Text = c.ClientExeDebug;
        CfgClientExeNormal.Text = c.ClientExeNormal;
        CfgPort.Text = c.Port.ToString();
        CfgMission.Text = c.Mission;
        CfgPlayerName.Text = c.PlayerName;
        CfgConfigName.Text = c.ConfigName;
        CfgConnectIp.Text = c.ConnectIp;
        CfgScanRoots.Text = string.Join("\n", c.ScanRoots);
        ConfigError.Visibility = Visibility.Collapsed;
    }

    private void OnRevertConfig(object sender, RoutedEventArgs e) => LoadSettingsFields();

    private void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CfgPort.Text.Trim(), out var port))
        {
            ConfigError.Text = "Port must be an integer.";
            ConfigError.Visibility = Visibility.Visible;
            return;
        }
        ConfigError.Visibility = Visibility.Collapsed;

        var roots = CfgScanRoots.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        var edited = _vm.Cfg with
        {
            DayzPath = CfgDayzPath.Text.Trim(),
            DayzToolsPath = CfgDayzToolsPath.Text.Trim(),
            ProfilesPath = CfgProfilesPath.Text.Trim(),
            ClientProfilesPath = CfgClientProfilesPath.Text.Trim(),
            ExeDebug = CfgExeDebug.Text.Trim(),
            ExeNormal = CfgExeNormal.Text.Trim(),
            ClientExeDebug = CfgClientExeDebug.Text.Trim(),
            ClientExeNormal = CfgClientExeNormal.Text.Trim(),
            Port = port,
            Mission = CfgMission.Text.Trim(),
            PlayerName = CfgPlayerName.Text.Trim(),
            ConfigName = CfgConfigName.Text.Trim(),
            ConnectIp = CfgConnectIp.Text.Trim(),
            ScanRoots = roots,
        };
        _vm.ApplyConfig(edited);
        LoadSettingsFields();
        LoadParamsEditor();
    }

    private void OnAddScanRoot(object sender, RoutedEventArgs e)
    {
        var dir = PickFolder();
        if (dir is null) return;
        var existing = CfgScanRoots.Text.TrimEnd();
        CfgScanRoots.Text = existing.Length == 0 ? dir : existing + "\n" + dir;
    }

    /// <summary>Current edited-or-saved DayZ install dir (Settings field wins over the VM config).</summary>
    private string CurrentDayzPath()
    {
        var p = CfgDayzPath?.Text?.Trim();
        return string.IsNullOrEmpty(p) ? _vm.Cfg.DayzPath : p;
    }

    // Mission folder picker → write a rel-or-abs path (relative to DayzPath when under it).
    private void OnBrowseMission(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var mpmissions = Path.Combine(dayz, "mpmissions");
        var dlg = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(mpmissions) ? mpmissions : dayz,
        };
        if (dlg.ShowDialog(this) == true)
            CfgMission.Text = RelOrAbs(dlg.FolderName, dayz);
    }

    // Server config (*.cfg) file picker → write a rel-or-abs path (relative to DayzPath when under it).
    private void OnBrowseConfigName(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var dlg = new OpenFileDialog
        {
            Filter = "Server config (*.cfg)|*.cfg|All files (*.*)|*.*",
            InitialDirectory = dayz,
        };
        if (dlg.ShowDialog(this) == true)
            CfgConfigName.Text = RelOrAbs(dlg.FileName, dayz);
    }

    /// <summary>
    /// Store <paramref name="fullPath"/> relative to <paramref name="dayzPath"/> when it lives under
    /// the DayZ install dir, else the absolute path. Mirrors how Core's ArgvBuilder resolves
    /// profiles paths so -mission=/-config= stay portable against the DayZ working dir.
    /// </summary>
    private static string RelOrAbs(string fullPath, string dayzPath)
    {
        var full = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(dayzPath);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    // --- Params editor: one editor that loads/saves the selected target+mode slot ---

    private string SelectedTarget =>
        (ParamTarget.SelectedItem as ComboBoxItem)?.Content as string ?? "server";

    private string SelectedParamMode =>
        (ParamMode.SelectedItem as ComboBoxItem)?.Content as string ?? "debug";

    private void OnParamSlotChanged(object sender, SelectionChangedEventArgs e) => LoadParamsEditor();

    private void LoadParamsEditor()
    {
        if (ParamsEditor is null) return; // not yet templated
        ParamsEditor.Text = string.Join("\n", _vm.CurrentParams(SelectedTarget, SelectedParamMode));
    }

    private void OnResetParams(object sender, RoutedEventArgs e) =>
        ParamsEditor.Text = string.Join("\n", MainViewModel.DefaultParams(SelectedTarget, SelectedParamMode));

    private void OnSaveParams(object sender, RoutedEventArgs e)
    {
        var lines = ParamsEditor.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _vm.ApplyParams(SelectedTarget, SelectedParamMode, lines);
    }

    // === Shared pickers / folder open =====================================

    /// <summary>Show a folder picker (OpenFolderDialog on .NET 8 WPF); null if cancelled.</summary>
    private string? PickFolder()
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    /// <summary>Browse into a named Settings TextBox. Tag form: "dir:&lt;FieldName&gt;".</summary>
    private void OnBrowseInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var parts = tag.Split(':', 2);
        if (parts.Length != 2) return;
        var dir = PickFolder();
        if (dir is null) return;
        if (FindName(parts[1]) is TextBox tb) tb.Text = dir;
    }
}
