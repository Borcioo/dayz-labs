using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Add-type modal for the Economy page: a class-name field plus a "Target file" selector listing
/// the mission's resolved Types files (defaulting to the primary/vanilla file). The chosen file becomes the
/// new entry's source file so <c>TypesService.SaveAll</c> routes it to that file. Returns
/// <c>(name, targetFile)</c> or null if cancelled.</summary>
internal static class NewTypeDialog
{
    public static (string name, string targetFile)? Show(
        Window owner, IReadOnlyList<(string Name, string Path)> targets,
        string title = "Add type", string defaultName = "", string okLabel = "Add")
    {
        // Use FluentWindow so it inherits the app's WPF-UI Fluent dark theme resources (Mica backdrop,
        // correct control styles) — consistent with other dialogs (ReleaseDialog, ServerEditorWindow, etc.).
        var win = new FluentWindow
        {
            Title = title,
            Owner = owner,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowBackdropType = WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };

        var root = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };

        // TitleBar matches the rest of the app's Fluent dialogs.
        var titleBar = new TitleBar
        {
            Title = title,
            ShowMaximize = false,
            ShowMinimize = false,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(titleBar);

        root.Children.Add(new System.Windows.Controls.TextBlock { Text = "Class name (e.g. AKM):", FontSize = 11, Opacity = 0.7 });
        var nameBox = new Wpf.Ui.Controls.TextBox { Margin = new Thickness(0, 2, 0, 12), Text = defaultName };
        root.Children.Add(nameBox);

        root.Children.Add(new System.Windows.Controls.TextBlock { Text = "Target file:", FontSize = 11, Opacity = 0.7 });
        var fileCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var t in targets) fileCombo.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Path });
        if (fileCombo.Items.Count > 0) fileCombo.SelectedIndex = 0;
        root.Children.Add(fileCombo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var add = new Wpf.Ui.Controls.Button { Content = okLabel, Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Appearance = ControlAppearance.Primary };
        var cancel = new Wpf.Ui.Controls.Button { Content = "Cancel", Width = 90, IsCancel = true };
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
        var target = (fileCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return (nameBox.Text.Trim(), target);
    }
}
