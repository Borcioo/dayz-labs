using System.IO;
using System.Windows;
using System.Windows.Media;
using Dzl.Core.App;
using Dzl.Core.Build.Preflight;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Per-mod build console: preflight findings (rule, message, file:line with severity
/// badges), build options, the live AddonBuilder log and failure diagnostics. Modeless and
/// ownerless on purpose — closing an owned Mica window hides its owner (WPF-UI quirk).</summary>
public partial class BuildWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly string _mod;
    private string _reportPath = "";
    private string _buildDir = "";

    public BuildWindow(MainViewModel vm, string mod)
    {
        _vm = vm;
        _mod = mod;
        InitializeComponent();
        Title = TitleBarCtl.Title = $"Build — {mod}";
        _vm.PropertyChanged += OnVmChanged;
        Unloaded += (_, _) => _vm.PropertyChanged -= OnVmChanged;
        LogBox.Text = _vm.BuildLog;
        RefreshPlan();
    }

    /// <summary>Pre-resolve the build plan: the key dropdown lists what the keys folder holds
    /// (managed in Settings → Signing) with the configured/default key pre-selected; no keys at
    /// all disables signing. A not-ready environment shows up front.</summary>
    private void RefreshPlan()
    {
        try
        {
            var svc = new BuildService(_vm.ConfigFilePath);
            var plan = svc.Plan(_mod);
            var keys = svc.ListKeys();

            KeyCombo.ItemsSource = keys.Select(k => k.Name).ToList();
            KeyCombo.SelectedItem = keys.Any(k => k.Name.Equals(plan.KeyName, StringComparison.OrdinalIgnoreCase))
                ? keys.First(k => k.Name.Equals(plan.KeyName, StringComparison.OrdinalIgnoreCase)).Name
                : keys.FirstOrDefault()?.Name;

            SignChk.IsEnabled = keys.Count > 0;
            SignChk.ToolTip = keys.Count > 0
                ? "Sign with the selected key + ship the .bikey"
                : "No signing keys yet — create or import one in Settings → Signing, then reopen this window.";
            if (keys.Count == 0) SignChk.IsChecked = false;
            StatusText.Text = plan.Ready ? $"ready — output: {plan.AddonsDir}" : plan.Message;

            // A previous build's output may already be there — openable from the start.
            _buildDir = plan.OutputDir;
            BuildFolderBtn.IsEnabled = Directory.Exists(_buildDir);
        }
        catch { /* plan is advisory; the build itself re-validates */ }
    }

    private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.BuildLog))
            Dispatcher.BeginInvoke(() => { LogBox.Text = _vm.BuildLog; LogBox.ScrollToEnd(); });
        if (e.PropertyName == nameof(MainViewModel.Building))
            Dispatcher.BeginInvoke(() => BuildBtn.IsEnabled = PreflightBtn.IsEnabled = !_vm.Building);
    }

    /// <summary>Render a preflight result into the findings pane (shared by the Preflight button
    /// and the build gate, which runs the same checks).</summary>
    private void ShowFindings(Dzl.Core.Build.Preflight.PreflightView r)
    {
        FindingsHint.Visibility = r.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FindingsHint.Text = "No findings — clean.";
        var modDir = _vm.ModDirOf(_mod);
        FindingsList.ItemsSource = r.Findings
            .OrderByDescending(f => f.Severity)
            .Select(f => new FindingRow(f, modDir))
            .ToList();
        SummaryText.Text = $"{(r.Ok ? "✓" : "✗")} {r.Errors} error(s), {r.Warnings} warning(s), {r.Infos} info";
        _reportPath = r.ReportTxt;
        ReportBtn.IsEnabled = _reportPath.Length > 0;
    }

    private async void OnPreflight(object sender, RoutedEventArgs e)
    {
        PreflightBtn.IsEnabled = false;
        StatusText.Text = "preflight running…";
        try
        {
            var r = await _vm.PreflightAsync(_mod);
            ShowFindings(r);
            StatusText.Text = r.Ok ? "preflight passed" : "preflight found errors — fix them before building";
        }
        catch (Exception ex) { StatusText.Text = "✗ " + ex.Message; }
        finally { PreflightBtn.IsEnabled = !_vm.Building; }
    }

    private async void OnBuild(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "building…";
        var result = await _vm.BuildModAsync(_mod,
            clean: CleanChk.IsChecked == true,
            binarize: BinarizeChk.IsChecked != false,
            sign: SignChk.IsChecked == true,
            force: ForceChk.IsChecked == true,
            keyName: SignChk.IsChecked == true ? KeyCombo.SelectedItem as string : null);
        if (result is null) { StatusText.Text = "a build is already running"; return; }
        // The gate ran the same preflight — surface its findings + report here too.
        if (result.Preflight is { } pf) ShowFindings(pf);
        if (result.ModDir.Length > 0) { _buildDir = result.ModDir; }
        BuildFolderBtn.IsEnabled = Directory.Exists(_buildDir);
        StatusText.Text = result.Ok ? $"✓ {result.Message}" : $"✗ {result.Message}";
    }

    private void OnOpenReport(object sender, RoutedEventArgs e)
    {
        // ShellOpen.Folder shell-executes any path; a .txt opens in the default editor.
        if (_reportPath.Length > 0) ShellOpen.Folder(_reportPath);
    }

    private void OnOpenBuildFolder(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_buildDir)) ShellOpen.Folder(_buildDir);
    }

    /// <summary>Open a finding's file in the configured editor (Settings → editor), jumping to the
    /// line when the editor supports it. Falls back to the OS default app when none is set.</summary>
    private void OnOpenFinding(object sender, RoutedEventArgs e)
    {
        // Hyperlink is a FrameworkContentElement, not a FrameworkElement.
        var ctx = (sender as FrameworkElement)?.DataContext ?? (sender as FrameworkContentElement)?.DataContext;
        if (ctx is not FindingRow row || row.FullPath.Length == 0) return;
        if (!Dzl.Core.Tools.EditorLauncher.OpenFile(_vm.Cfg.EditorPath, row.FullPath, row.Line, _vm.ModDirOf(_mod)))
            StatusText.Text = $"✗ could not open {row.Location} — set an editor in Settings";
    }

    /// <summary>Row adapter: severity → badge colors, file:line → one location string (clickable
    /// when the file resolves inside the mod dir).</summary>
    public sealed class FindingRow
    {
        public FindingRow(Finding f, string modDir)
        {
            Severity = f.Severity switch
            {
                FindingSeverity.Error => "ERROR",
                FindingSeverity.Warning => "WARN",
                _ => "info",
            };
            Rule = f.Rule;
            Message = f.Message;
            Line = f.Line;
            Location = f.File.Length == 0 ? "" : f.Line > 0 ? $"{f.File}:{f.Line}" : f.File;
            if (f.File.Length > 0)
            {
                var full = System.IO.Path.Combine(modDir, f.File.Replace('\\', System.IO.Path.DirectorySeparatorChar));
                FullPath = System.IO.File.Exists(full) ? full : "";
            }
            (BadgeBg, BadgeFg) = f.Severity switch
            {
                FindingSeverity.Error => (Brush("#4D1F1F"), Brush("#FF6B6B")),
                FindingSeverity.Warning => (Brush("#4D3D1F"), Brush("#FFC966")),
                _ => (Brush("#1F3A4D"), Brush("#7CC4E3")),
            };
        }

        public string Severity { get; }
        public string Rule { get; }
        public string Message { get; }
        public string Location { get; }
        public string FullPath { get; } = "";
        public int Line { get; }
        public bool Openable => FullPath.Length > 0;
        public SolidColorBrush BadgeBg { get; }
        public SolidColorBrush BadgeFg { get; }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}
