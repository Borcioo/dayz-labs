using System;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>Pre-resolve the build plan: signing-key availability gates the sign checkbox
    /// (keys are managed in Settings → Signing), and a not-ready environment shows up front.</summary>
    private void RefreshPlan()
    {
        try
        {
            var plan = new BuildService(_vm.ConfigFilePath).Plan(_mod);
            SignChk.IsEnabled = plan.HasKey;
            SignChk.ToolTip = plan.HasKey
                ? $"Sign with key '{plan.KeyName}' + ship the .bikey"
                : "No signing key yet — create one in Settings → Signing, then reopen this window.";
            if (!plan.HasKey) SignChk.IsChecked = false;
            StatusText.Text = plan.Ready ? $"ready — output: {plan.AddonsDir}" : plan.Message;
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

    private async void OnPreflight(object sender, RoutedEventArgs e)
    {
        PreflightBtn.IsEnabled = false;
        StatusText.Text = "preflight running…";
        try
        {
            var r = await _vm.PreflightAsync(_mod);
            FindingsHint.Visibility = r.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FindingsHint.Text = "No findings — clean.";
            FindingsList.ItemsSource = r.Findings
                .OrderByDescending(f => f.Severity)
                .Select(f => new FindingRow(f))
                .ToList();
            SummaryText.Text = $"{(r.Ok ? "✓" : "✗")} {r.Errors} error(s), {r.Warnings} warning(s), {r.Infos} info";
            _reportPath = r.ReportTxt;
            ReportBtn.IsEnabled = _reportPath.Length > 0;
            StatusText.Text = r.Ok ? "preflight passed" : "preflight found errors — fix them before building";
        }
        catch (Exception ex) { StatusText.Text = "✗ " + ex.Message; }
        finally { PreflightBtn.IsEnabled = !_vm.Building; }
    }

    private async void OnBuild(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "building…";
        await _vm.BuildModAsync(_mod,
            clean: CleanChk.IsChecked == true,
            binarize: BinarizeChk.IsChecked != false,
            sign: SignChk.IsChecked == true,
            force: ForceChk.IsChecked == true);
        StatusText.Text = "done — see the log";
    }

    private void OnOpenReport(object sender, RoutedEventArgs e)
    {
        // ShellOpen.Folder shell-executes any path; a .txt opens in the default editor.
        if (_reportPath.Length > 0) ShellOpen.Folder(_reportPath);
    }

    /// <summary>Row adapter: severity → badge colors, file:line → one location string.</summary>
    public sealed class FindingRow
    {
        public FindingRow(Finding f)
        {
            Severity = f.Severity switch
            {
                FindingSeverity.Error => "ERROR",
                FindingSeverity.Warning => "WARN",
                _ => "info",
            };
            Rule = f.Rule;
            Message = f.Message;
            Location = f.File.Length == 0 ? "" : f.Line > 0 ? $"{f.File}:{f.Line}" : f.File;
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
        public SolidColorBrush BadgeBg { get; }
        public SolidColorBrush BadgeFg { get; }

        private static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }
}
