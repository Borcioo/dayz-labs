using System.Windows;
using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>The "Map files" tab: lists the mission's auto-generated map-data files and opens them externally
/// (VS Code / reveal / mission folder). No in-app editing — these are exported terrain data. DataContext =
/// a <see cref="MapFilesVm"/>.</summary>
public partial class MapFilesEditor : UserControl
{
    public MapFilesEditor()
    {
        InitializeComponent();
    }

    private MapFilesVm? Vm => DataContext as MapFilesVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => ShellOpen.Folder(Vm?.MissionDir);

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MapFileVm f) ShellOpen.Editor(f.Path);
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MapFileVm f) ShellOpen.Reveal(f.Path);
    }
}
