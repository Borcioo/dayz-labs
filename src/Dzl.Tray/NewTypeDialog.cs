using System.Windows;
using System.Windows.Controls;

namespace Dzl.Tray;

/// <summary>Add-type modal for the Economy page: a class-name field plus a "Target file" selector listing
/// the mission's resolved Types files (defaulting to the primary/vanilla file). The chosen file becomes the
/// new entry's source file so <c>TypesService.SaveAll</c> routes it to that file. Returns
/// <c>(name, targetFile)</c> or null if cancelled.</summary>
internal static class NewTypeDialog
{
    public static (string name, string targetFile)? Show(
        Window owner, IReadOnlyList<(string Name, string Path)> targets)
    {
        var win = new Window
        {
            Title = "Add type",
            Owner = owner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock { Text = "Class name (e.g. AKM):", FontSize = 11, Opacity = 0.7 });
        var nameBox = new TextBox { Margin = new Thickness(0, 2, 0, 12) };
        root.Children.Add(nameBox);

        root.Children.Add(new TextBlock { Text = "Target file:", FontSize = 11, Opacity = 0.7 });
        var fileCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 4), DisplayMemberPath = "Name" };
        foreach (var t in targets) fileCombo.Items.Add(t);
        if (fileCombo.Items.Count > 0) fileCombo.SelectedIndex = 0;
        root.Children.Add(fileCombo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var add = new Button { Content = "Add", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        buttons.Children.Add(add);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
            win.DialogResult = true;
        };
        win.Content = root;
        nameBox.Focus();

        if (win.ShowDialog() != true || string.IsNullOrWhiteSpace(nameBox.Text)) return null;
        var target = fileCombo.SelectedItem is ValueTuple<string, string> sel ? sel.Item2 : "";
        return (nameBox.Text.Trim(), target);
    }
}
