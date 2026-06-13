using System.Windows.Controls;

namespace Dzl.Tray.Controls;

/// <summary>Economy dashboard tab — stat tiles + aggregated validation. All logic lives on
/// <see cref="CeDashboardVm"/> (the DataContext); tile/finding clicks raise NavigateRequested, which
/// the host <see cref="EconomyPanel"/> turns into a tab switch.</summary>
public partial class EconomyDashboard : UserControl
{
    public EconomyDashboard() => InitializeComponent();
}
