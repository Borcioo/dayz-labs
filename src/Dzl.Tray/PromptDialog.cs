using System.Windows;
using System.Windows.Controls;

namespace Dzl.Tray;

/// <summary>A minimal single-line text prompt (code-built, no XAML) for the few spots that need one ad-hoc
/// value — e.g. a release tag. Returns the entered text, or null if cancelled.</summary>
internal static class PromptDialog
{
    public static string? Show(Window owner, string title, string prompt, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Owner = owner,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

        var box = new TextBox { Text = initial };
        panel.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        win.Content = panel;
        ok.Click += (_, _) => { win.DialogResult = true; };
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        return win.ShowDialog() == true ? box.Text : null;
    }
}
