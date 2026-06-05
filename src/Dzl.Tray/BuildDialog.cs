using System.Windows;
using System.Windows.Controls;
using Dzl.Core.App;

namespace Dzl.Tray;

/// <summary>Build-options modal opened from the My Mods "Build" button: shows the resolved (pre-filled)
/// source/output/tool paths read-only, exposes the same flags as <c>dzl build</c> (--clean / --no-binarize)
/// as checkboxes with a live CLI preview, and returns the chosen flags (or null if cancelled).</summary>
internal static class BuildDialog
{
    public static (bool clean, bool binarize)? Show(Window owner, BuildService.BuildPlanView plan)
    {
        var win = new Window
        {
            Title = $"Build {plan.Mod}",
            Owner = owner,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = plan.Ready ? "Ready to build." : "⚠ " + plan.Message,
            Margin = new Thickness(0, 0, 0, 12),
            FontWeight = plan.Ready ? FontWeights.Normal : FontWeights.SemiBold,
        });

        root.Children.Add(Field("Project (source)", plan.ProjectDir));
        root.Children.Add(Field("Reached on P: via junction", plan.SourceOnP));
        root.Children.Add(Field("Output (PBO → Addons)", plan.AddonsDir));
        root.Children.Add(Field("AddonBuilder", plan.AddonBuilderExe));

        var binarize = new CheckBox { Content = "Binarize configs/models (default)", IsChecked = true, Margin = new Thickness(0, 8, 0, 4) };
        var clean = new CheckBox { Content = "Clean output first (-clear)", IsChecked = false, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(binarize);
        root.Children.Add(clean);

        var cli = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 0, 0, 4), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        void RefreshCli()
        {
            var flags = "";
            if (clean.IsChecked == true) flags += " --clean";
            if (binarize.IsChecked != true) flags += " --no-binarize";
            cli.Text = $"dzl build {plan.Mod}{flags}";
        }
        clean.Checked += (_, _) => RefreshCli(); clean.Unchecked += (_, _) => RefreshCli();
        binarize.Checked += (_, _) => RefreshCli(); binarize.Unchecked += (_, _) => RefreshCli();
        RefreshCli();
        root.Children.Add(new TextBlock { Text = "CLI equivalent", FontSize = 11, Opacity = 0.7 });
        root.Children.Add(cli);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var build = new Button { Content = "Build", Width = 90, IsDefault = true, IsEnabled = plan.Ready, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        buttons.Children.Add(build);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        build.Click += (_, _) => { win.DialogResult = true; };
        win.Content = root;

        return win.ShowDialog() == true ? (clean.IsChecked == true, binarize.IsChecked == true) : null;
    }

    private static StackPanel Field(string label, string value)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Opacity = 0.7 });
        sp.Children.Add(new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.NoWrap });
        return sp;
    }
}
