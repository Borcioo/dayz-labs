using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The Server "Messages" editor (db/messages.xml). Presentational only — forwards to the bound
/// <see cref="MessagesVm"/>. DataContext = a <see cref="MessagesVm"/>.</summary>
public partial class MessagesEditor : UserControl
{
    public MessagesEditor()
    {
        InitializeComponent();
    }

    private MessagesVm? Vm => DataContext as MessagesVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddClick(object sender, RoutedEventArgs e) => Vm?.AddMessage();

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ServerMessageVm m) Vm?.RemoveMessage(m);
    }

    private void OnFieldLostFocus(object sender, RoutedEventArgs e) => Commit(sender);

    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(sender); e.Handled = true; }
    }

    private static void Commit(object sender)
    {
        if (sender is FrameworkElement { DataContext: ServerMessageVm m }) m.Commit();
    }
}
