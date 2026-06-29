using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.App;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Pack build console: pick which inner mods to build (all selected by default), preflight them
/// (findings shown per mod in tabs, same UX as <see cref="BuildWindow"/>), binarize/sign, then build them
/// into one <c>@&lt;pack&gt;</c> (Addons with a PBO per mod + keys\). Modeless + ownerless like BuildWindow.</summary>
public partial class PackBuildWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private readonly string _pack;
    private string _outputDir = "";

    public PackBuildWindow(MainViewModel vm, ModProjectVm pack)
    {
        _vm = vm;
        _pack = pack.Name;
        InitializeComponent();
        Title = TitleBarCtl.Title = $"Build pack — {pack.Name}";
        IntroText.Text = $"Builds the selected mods into one @{pack.Name} — each inner mod becomes its own PBO " +
                         "under Addons\\, with a shared keys\\. Building a subset swaps just those PBOs and keeps " +
                         "the rest; building all does a clean rebuild.";
        ChildList.ItemsSource = pack.Children.Select(c => new Pick(c.Name, c.Path)).ToList();
        UpdateCount();
        LogBox.Text = _vm.BuildLog;
        _vm.PropertyChanged += OnVmChanged;
        Unloaded += (_, _) => _vm.PropertyChanged -= OnVmChanged;
        LoadKeys();
    }

    private void LoadKeys()
    {
        try
        {
            var svc = new BuildService(_vm.ConfigFilePath);
            var keys = svc.ListKeys();
            var defaultKey = svc.Plan(_pack).KeyName;
            KeyCombo.ItemsSource = keys.Select(k => k.Name).ToList();
            KeyCombo.SelectedItem = keys.Any(k => k.Name.Equals(defaultKey, System.StringComparison.OrdinalIgnoreCase))
                ? keys.First(k => k.Name.Equals(defaultKey, System.StringComparison.OrdinalIgnoreCase)).Name
                : keys.FirstOrDefault()?.Name;
            SignChk.IsEnabled = keys.Count > 0;
            if (keys.Count == 0) { SignChk.IsChecked = false; SignChk.ToolTip = "No signing keys — create one in Settings → Signing."; }
        }
        catch { /* advisory; the build re-validates */ }
    }

    private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.BuildLog))
            Dispatcher.BeginInvoke(() => { LogBox.Text = _vm.BuildLog; LogBox.ScrollToEnd(); });
        if (e.PropertyName == nameof(MainViewModel.Building))
            Dispatcher.BeginInvoke(() => BuildBtn.IsEnabled = PreflightBtn.IsEnabled = !_vm.Building);
    }

    private List<string> Selected() =>
        (ChildList.ItemsSource as IEnumerable<Pick>)?.Where(p => p.IsSelected).Select(p => p.Name).ToList()
        ?? new List<string>();

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        var all = SelectAllChk.IsChecked == true;
        foreach (var p in (ChildList.ItemsSource as IEnumerable<Pick>) ?? Enumerable.Empty<Pick>())
            p.IsSelected = all;
        UpdateCount();
    }

    private void OnChildToggled(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var picks = (ChildList.ItemsSource as IEnumerable<Pick>)?.ToList() ?? new List<Pick>();
        var sel = picks.Count(p => p.IsSelected);
        CountText.Text = $"· {sel}/{picks.Count} selected";
    }

    private async void OnPreflight(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { StatusText.Text = "select at least one mod"; return; }
        PreflightBtn.IsEnabled = false;
        StatusText.Text = "preflight running…";
        try
        {
            var reports = await _vm.PreflightPackAsync(_pack, selected);
            var tabs = reports.Select(r => new ChildTab
            {
                Header = r.View.Errors > 0 ? $"{r.Child}  ✗" : r.View.Warnings > 0 ? $"{r.Child}  ⚠" : $"{r.Child}  ✓",
                Summary = $"{(r.View.Ok ? "✓" : "✗")} {r.View.Errors} error(s), {r.View.Warnings} warning(s), {r.View.Infos} info",
                Findings = r.View.Findings.OrderByDescending(f => f.Severity)
                    .Select(f => new BuildWindow.FindingRow(f, r.Dir)).ToList(),
            }).ToList();

            ChildTabs.ItemsSource = tabs;
            FindingsHint.Visibility = tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // Focus the first mod with errors so problems are front-and-centre.
            var firstBad = tabs.FindIndex(t => t.Header.EndsWith("✗"));
            ChildTabs.SelectedIndex = firstBad >= 0 ? firstBad : 0;

            var errs = reports.Sum(r => r.View.Errors);
            StatusText.Text = errs == 0 ? "preflight passed" : $"preflight found {errs} error(s) — fix before building";
        }
        catch (System.Exception ex) { StatusText.Text = "✗ " + ex.Message; }
        finally { PreflightBtn.IsEnabled = !_vm.Building; }
    }

    private async void OnBuild(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) { StatusText.Text = "select at least one mod"; return; }

        StatusText.Text = "building…";
        var r = await _vm.BuildPackAsync(_pack, selected,
            binarize: BinarizeChk.IsChecked != false, sign: SignChk.IsChecked == true,
            keyName: SignChk.IsChecked == true ? KeyCombo.SelectedItem as string : null,
            ignorePreflightErrors: BuildAnywayChk.IsChecked == true);
        if (r is null) { StatusText.Text = "a build is already running"; return; }

        _outputDir = r.OutputDir;
        FolderBtn.IsEnabled = Directory.Exists(_outputDir);
        StatusText.Text = r.Ok ? $"✓ {r.Message}" : $"✗ {r.Message}";
    }

    /// <summary>Open a finding's file in the configured editor at its line (same as the single-mod build).</summary>
    private void OnOpenFinding(object sender, RoutedEventArgs e)
    {
        var ctx = (sender as FrameworkElement)?.DataContext ?? (sender as FrameworkContentElement)?.DataContext;
        if (ctx is not BuildWindow.FindingRow row || row.FullPath.Length == 0) return;
        var wd = Path.GetDirectoryName(row.FullPath) ?? "";
        if (!Dzl.Core.Tools.EditorLauncher.OpenFile(_vm.Cfg.EditorPath, row.FullPath, row.Line, wd))
            StatusText.Text = $"✗ could not open {row.Location} — set an editor in Settings";
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_outputDir)) ShellOpen.Folder(_outputDir);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>One selectable inner mod in the pick list.</summary>
    private sealed partial class Pick : ObservableObject
    {
        public string Name { get; }
        public string Marker { get; }
        [ObservableProperty] private bool _isSelected = true;
        public Pick(string name, string dir)
        {
            Name = name;
            Marker = File.Exists(Path.Combine(dir, "config.cpp")) ? "config.cpp" : "$PBOPREFIX$";
        }
    }

    /// <summary>One preflight tab (per inner mod): header, summary and the findings rows.</summary>
    private sealed class ChildTab
    {
        public string Header { get; init; } = "";
        public string Summary { get; init; } = "";
        public IReadOnlyList<BuildWindow.FindingRow> Findings { get; init; } = System.Array.Empty<BuildWindow.FindingRow>();
        public bool HasFindings => Findings.Count > 0;
    }
}
