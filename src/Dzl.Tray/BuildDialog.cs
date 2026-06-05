using System.Windows;
using System.Windows.Controls;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray;

/// <summary>Build-options modal opened from the My Mods "Build" button: shows the resolved (pre-filled)
/// source/output/tool paths read-only, exposes the same flags as <c>dzl build</c> (--clean / --no-binarize /
/// --sign) as checkboxes with a live CLI preview, and offers to generate the signing key inline when one
/// isn't set up yet. Returns the chosen flags (or null if cancelled).</summary>
internal static class BuildDialog
{
    public static (bool clean, bool binarize, bool sign)? Show(Window owner, MainViewModel vm, string mod)
    {
        var plan = vm.BuildPlan(mod);

        var win = new Window
        {
            Title = $"Build {plan.Mod}",
            Owner = owner,
            Width = 580,
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
        var clean = new CheckBox { Content = "Clean output first (-clear)", IsChecked = false, Margin = new Thickness(0, 0, 0, 4) };
        var sign = new CheckBox { Content = "Sign the PBO with your key", IsChecked = false, Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(binarize);
        root.Children.Add(clean);
        root.Children.Add(sign);

        // Signing key status + inline generate. The key is per-creator (one key signs all your mods).
        var keyStatus = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        var genKey = new Button { Content = "Generate signing key", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(keyStatus);
        root.Children.Add(genKey);

        var cli = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 0, 0, 4), FontFamily = new System.Windows.Media.FontFamily("Consolas") };

        void ApplyKeyState(bool hasKey, string keyName)
        {
            sign.IsEnabled = hasKey;
            if (!hasKey) sign.IsChecked = false;
            genKey.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
            keyStatus.Text = hasKey
                ? $"Signing key: {keyName} ✓"
                : (keyName.Length == 0
                    ? "No signing key configured — set a name in Settings → Signing, or generate one (uses your author handle)."
                    : $"Signing key '{keyName}' not created yet — generate it to enable signing.");
        }

        void RefreshCli()
        {
            var flags = "";
            if (clean.IsChecked == true) flags += " --clean";
            if (binarize.IsChecked != true) flags += " --no-binarize";
            if (sign.IsChecked == true) flags += " --sign";
            cli.Text = $"dzl build {plan.Mod}{flags}";
        }

        clean.Checked += (_, _) => RefreshCli(); clean.Unchecked += (_, _) => RefreshCli();
        binarize.Checked += (_, _) => RefreshCli(); binarize.Unchecked += (_, _) => RefreshCli();
        sign.Checked += (_, _) => RefreshCli(); sign.Unchecked += (_, _) => RefreshCli();

        genKey.Click += (_, _) =>
        {
            genKey.IsEnabled = false;
            keyStatus.Text = "Generating key…";
            var msg = vm.GenerateSigningKey();
            var fresh = vm.BuildPlan(mod);   // re-plan to pick up the new key
            ApplyKeyState(fresh.HasKey, fresh.KeyName);
            if (!fresh.HasKey) keyStatus.Text = msg;   // surface the error
            genKey.IsEnabled = true;
            RefreshCli();
        };

        ApplyKeyState(plan.HasKey, plan.KeyName);
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

        return win.ShowDialog() == true
            ? (clean.IsChecked == true, binarize.IsChecked == true, sign.IsChecked == true)
            : null;
    }

    private static StackPanel Field(string label, string value)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Opacity = 0.7 });
        sp.Children.Add(new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.NoWrap });
        return sp;
    }
}
