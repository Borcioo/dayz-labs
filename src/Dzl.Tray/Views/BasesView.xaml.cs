using System.Windows;
using System.Windows.Controls;
using Dzl.Tray;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>Bases page (server templates): create from the DayZ install or empty, list / open /
/// delete bases. All state lives on <see cref="MainViewModel"/> (the inherited DataContext).</summary>
public partial class BasesView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public BasesView() => InitializeComponent();

    private void OnCreateBaseFromInstall(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var name = NewBaseNameBox.Text.Trim();
        if (name.Length == 0) { NewBaseStatus.Text = "Enter a base name."; return; }
        var map = (NewBaseMapBox.SelectedItem as string) ?? "chernarus";
        NewBaseStatus.Text = Vm.CreateBaseFromInstall(name, map);
        if (NewBaseStatus.Text.StartsWith('✓')) NewBaseNameBox.Text = "";
    }

    private void OnCreateEmptyBase(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var name = NewBaseNameBox.Text.Trim();
        if (name.Length == 0) { NewBaseStatus.Text = "Enter a base name."; return; }
        NewBaseStatus.Text = Vm.CreateEmptyBase(name);
        if (NewBaseStatus.Text.StartsWith('✓')) NewBaseNameBox.Text = "";
    }

    private void OnDeleteBase(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is not FrameworkElement { Tag: string name }) return;
        var ok = System.Windows.MessageBox.Show(
            $"Delete base \"{name}\"?\n\nThis removes the template folder and all its files. " +
            "Existing instances created from it are not affected.",
            "Delete base", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        NewBaseStatus.Text = Vm.DeleteBase(name);
    }

    private void OnOpenBaseFolder(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is not FrameworkElement { Tag: string name }) return;
        var dir = Vm.BaseDirOf(name);
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}", "Open base folder",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }
}
