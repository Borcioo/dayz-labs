using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The CE "Ignore list" editor (cfgignorelist.xml). Presentational only — add/remove forward to the
/// bound <see cref="IgnoreListVm"/>. DataContext = an <see cref="IgnoreListVm"/>.</summary>
public partial class IgnoreListEditor : UserControl
{
    public IgnoreListEditor()
    {
        InitializeComponent();
    }

    private IgnoreListVm? Vm => DataContext as IgnoreListVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnAddClick(object sender, RoutedEventArgs e) => Vm?.AddName();

    private void OnAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.AddName(); e.Handled = true; }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name) Vm?.RemoveName(name);
    }
}
