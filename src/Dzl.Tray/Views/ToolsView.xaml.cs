using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Build.Preflight;
using Dzl.Core.Tools;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;

namespace Dzl.Tray.Views;

/// <summary>Tools page: the DayZ Tools catalog, P: work-drive controls and the three inline
/// tool runs (Pack PBO, Batch PAA, Unbinarize). All state lives on <see cref="MainViewModel"/>
/// (the inherited DataContext); the runs are async with their own status TextBoxes.</summary>
public partial class ToolsView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public ToolsView() => InitializeComponent();

    /// <summary>Refresh the tool catalog and the per-tool "missing" hints / button-enabled state.
    /// Public so the host window can call it when the Tools page is shown.</summary>
    public void RefreshToolsPage()
    {
        if (Vm is null) return;
        Vm.RefreshTools();
        // Pack uses the in-process engine (PboWriter) — no external packer needed, so it's always available.
        var (keyNames, defKey) = Vm.SigningKeys();
        PackKeyCombo.ItemsSource = keyNames;
        PackKeyCombo.SelectedItem = defKey;
        PackSignChk.IsEnabled = keyNames.Count > 0;
        if (keyNames.Count == 0) { PackSignChk.IsChecked = false; PackSignChk.ToolTip = "No signing keys — create one in Settings → Signing."; }
        PaaToolMissing.Visibility = Vm.ToolExe("imagetopaa") is null ? Visibility.Visible : Visibility.Collapsed;
        PaaButton.IsEnabled = Vm.ToolExe("imagetopaa") is not null;
        UnbinToolMissing.Visibility = Vm.ToolExe("cfgconvert") is null ? Visibility.Visible : Visibility.Collapsed;
        UnbinButton.IsEnabled = Vm.ToolExe("cfgconvert") is not null;
    }

    private void OnRefreshTools(object sender, RoutedEventArgs e) => RefreshToolsPage();

    private static string Sev(FindingSeverity s) => s switch
    {
        FindingSeverity.Error => "✗",
        FindingSeverity.Warning => "⚠",
        _ => "ℹ",
    };

    private async void OnPreflightPack(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var src = PackSrcBox.Text.Trim();
        if (src.Length == 0 || !Directory.Exists(src)) { PackStatus.Text = "pick a source folder"; return; }
        PreflightPackButton.IsEnabled = false;
        PackStatus.Text = "preflight running…";
        PackOutput.Text = "preflight: checking folder…\n";
        try
        {
            var pf = await Vm.PreflightFolderAsync(src);
            foreach (var f in pf.Findings.OrderByDescending(f => f.Severity))
                PackOutput.AppendText($"{Sev(f.Severity)} {f.Rule}: {f.Message}" +
                    (f.File.Length > 0 ? $"  [{f.File}:{f.Line}]" : "") + "\n");
            PackOutput.AppendText($"\n{(pf.Ok ? "✓" : "✗")} {pf.Errors} error(s), {pf.Warnings} warning(s), {pf.Infos} info");
            PackStatus.Text = pf.Ok ? $"preflight ok ({pf.Warnings} warning(s))" : $"{pf.Errors} error(s) — fix or tick \"build anyway\"";
        }
        catch (Exception ex) { PackStatus.Text = "✗ " + ex.Message; }
        finally { PreflightPackButton.IsEnabled = true; PackOutput.ScrollToEnd(); }
    }

    private async void OnPackPbo(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var src = PackSrcBox.Text.Trim();
        var dst = PackDstBox.Text.Trim();
        if (src.Length == 0 || dst.Length == 0) { PackStatus.Text = "pick a source and output folder"; return; }
        if (!Directory.Exists(src)) { PackStatus.Text = "source folder not found"; return; }

        PackButton.IsEnabled = PreflightPackButton.IsEnabled = false;
        PackStatus.Text = "packing…";
        PackOutput.Text = "";
        var log = new System.Progress<string>(line => { PackOutput.AppendText(line + "\n"); PackOutput.ScrollToEnd(); });
        try
        {
            var r = await Vm.PackFolderAsync(src, dst, PackPrefixBox.Text,
                binarize: PackBinarizeChk.IsChecked == true,
                sign: PackSignChk.IsChecked == true,
                keyName: PackSignChk.IsChecked == true ? PackKeyCombo.SelectedItem as string : null,
                ignorePreflight: PackBuildAnywayChk.IsChecked == true,
                log);
            PackStatus.Text = r.Ok ? "✓ packed" : "✗ " + r.Message;
            PackOutput.AppendText("\n" + (r.Ok ? $"✓ {r.Message}" : $"✗ {r.Message}"));
        }
        catch (Exception ex) { PackStatus.Text = "✗ " + ex.Message; PackOutput.AppendText("\n✗ " + ex.Message); }
        finally { PackButton.IsEnabled = PreflightPackButton.IsEnabled = true; PackOutput.ScrollToEnd(); }
    }

    private async void OnConvertPaa(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var exe = Vm.ToolExe("imagetopaa");
        if (exe is null) { PaaOutput.Text = "ImageToPAA not found."; return; }
        var dir = PaaDirBox.Text.Trim();
        if (dir.Length == 0) { PaaOutput.Text = "Pick an image folder."; return; }
        var recursive = PaaRecursive.IsChecked == true;

        // First surface suffix warnings from the plan.
        var plan = Vm.PlanPaa(dir, recursive);
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
            await Vm.ConvertPaaAsync(exe, dir, recursive, progress);
            PaaOutput.AppendText($"Done. {ok} ok, {fail} failed.\n");
        }
        catch (Exception ex) { PaaOutput.AppendText("Error: " + ex.Message + "\n"); }
        finally { PaaButton.IsEnabled = true; PaaOutput.ScrollToEnd(); }
    }

    private async void OnUnbinarize(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var exe = Vm.ToolExe("cfgconvert");
        if (exe is null) { UnbinOutput.Text = "CfgConvert not found."; return; }
        var bin = BinFileBox.Text.Trim();
        if (bin.Length == 0) { UnbinOutput.Text = "Pick a .bin file."; return; }
        var outCpp = Path.ChangeExtension(bin, ".cpp");

        UnbinButton.IsEnabled = false;
        UnbinOutput.Text = "Unbinarizing…";
        try
        {
            var (ok, output) = await Vm.UnbinarizeAsync(exe, bin, outCpp);
            UnbinOutput.Text = $"{(ok ? "OK → " + outCpp : "FAILED")}\n{output}";
        }
        catch (Exception ex) { UnbinOutput.Text = "Error: " + ex.Message; }
        finally { UnbinButton.IsEnabled = true; UnbinOutput.ScrollToEnd(); }
    }

    // Folder picker → set the target TextBox text (Tag picks which one).
    private void OnBrowseFolderInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string which }) return;
        var current = which switch
        {
            "packsrc" => PackSrcBox.Text,
            "packdst" => PackDstBox.Text,
            "paadir" => PaaDirBox.Text,
            _ => "",
        };
        var dir = PickFolder(BrowseStartDir.Resolve(current, isFile: false,
            new[] { Vm?.ProjectsRoot, Vm?.Cfg.DayzPath }, Directory.Exists));
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
        var start = BrowseStartDir.Resolve(BinFileBox.Text, isFile: true,
            new[] { Vm?.ProjectsRoot, Vm?.Cfg.DayzPath }, Directory.Exists);
        var dlg = new OpenFileDialog
        {
            Filter = "Binarized config (*.bin)|*.bin|All files (*.*)|*.*",
            InitialDirectory = start,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) BinFileBox.Text = dlg.FileName;
    }

    /// <summary>Show a folder picker (OpenFolderDialog on .NET 8 WPF) starting at <paramref name="initialDir"/>;
    /// null if cancelled.</summary>
    private string? PickFolder(string? initialDir = null)
    {
        var dlg = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(initialDir)) dlg.InitialDirectory = initialDir;
        return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FolderName : null;
    }
}
