using System.Windows;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray;

/// <summary>
/// The launcher main window: mod checklist, server/client controls, profile
/// switcher and live log panes. Construction resolves the config path the same way
/// the tray does (<see cref="App.ConfigPath"/>) and wires a fresh
/// <see cref="MainViewModel"/> as the DataContext.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(App.ConfigPath());
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }
}
