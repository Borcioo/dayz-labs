using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dzl.Tray.Controls;

/// <summary>The World "Weather" editor (cfgweather.xml). Presentational only — forwards to the bound
/// <see cref="WeatherVm"/>. DataContext = a <see cref="WeatherVm"/>.</summary>
public partial class WeatherEditor : UserControl
{
    public WeatherEditor()
    {
        InitializeComponent();
    }

    private WeatherVm? Vm => DataContext as WeatherVm;

    private void OnReloadClick(object sender, RoutedEventArgs e) => Vm?.Reload();

    private void OnKnobLostFocus(object sender, RoutedEventArgs e) => Commit(sender);

    private void OnKnobKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(sender); e.Handled = true; }
    }

    private static void Commit(object sender)
    {
        if (sender is FrameworkElement { DataContext: WeatherKnobVm k }) k.Commit();
    }
}
