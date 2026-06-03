using System.Linq;
using System.Windows;
using Dzl.Core.Config;

namespace Dzl.Tray;

/// <summary>
/// Modal editor for the config scalars + scan roots. Starts from the supplied
/// <see cref="DzlConfig"/> and, on OK (with a valid integer Port), exposes the edited
/// config via <see cref="Result"/> and sets <c>DialogResult=true</c>.
/// </summary>
public partial class ConfigWindow : Window
{
    private readonly DzlConfig _input;

    /// <summary>The edited config, populated when the dialog closes with OK.</summary>
    public DzlConfig Result { get; private set; }

    public ConfigWindow(DzlConfig cfg)
    {
        InitializeComponent();
        _input = cfg;
        Result = cfg;

        DayzPath.Text = cfg.DayzPath;
        DayzToolsPath.Text = cfg.DayzToolsPath;
        ProfilesPath.Text = cfg.ProfilesPath;
        ClientProfilesPath.Text = cfg.ClientProfilesPath;
        ExeDebug.Text = cfg.ExeDebug;
        ExeNormal.Text = cfg.ExeNormal;
        ClientExeDebug.Text = cfg.ClientExeDebug;
        ClientExeNormal.Text = cfg.ClientExeNormal;
        Port.Text = cfg.Port.ToString();
        Mission.Text = cfg.Mission;
        PlayerName.Text = cfg.PlayerName;
        ConfigName.Text = cfg.ConfigName;
        ConnectIp.Text = cfg.ConnectIp;
        ScanRoots.Text = string.Join("\n", cfg.ScanRoots);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Port.Text.Trim(), out var port))
        {
            MessageBox.Show(this, "Port must be an integer.", "Invalid port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var roots = ScanRoots.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        Result = _input with
        {
            DayzPath = DayzPath.Text.Trim(),
            DayzToolsPath = DayzToolsPath.Text.Trim(),
            ProfilesPath = ProfilesPath.Text.Trim(),
            ClientProfilesPath = ClientProfilesPath.Text.Trim(),
            ExeDebug = ExeDebug.Text.Trim(),
            ExeNormal = ExeNormal.Text.Trim(),
            ClientExeDebug = ClientExeDebug.Text.Trim(),
            ClientExeNormal = ClientExeNormal.Text.Trim(),
            Port = port,
            Mission = Mission.Text.Trim(),
            PlayerName = PlayerName.Text.Trim(),
            ConfigName = ConfigName.Text.Trim(),
            ConnectIp = ConnectIp.Text.Trim(),
            ScanRoots = roots,
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
