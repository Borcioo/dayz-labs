using System.Windows;
using Wpf.Ui.Controls;

namespace Dzl.Tray.Dialogs;

/// <summary>Confirmation dialog for the in-app uninstall. Exposes the user's choice;
/// the caller runs <see cref="Uninstaller.Run"/> when <see cref="Confirmed"/> is true.</summary>
public partial class UninstallWindow : FluentWindow
{
    public bool Confirmed { get; private set; }
    public bool RemoveUserData { get; private set; }

    public UninstallWindow() => InitializeComponent();

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        RemoveUserData = WipeData.IsChecked == true;
        Close();
    }
}
