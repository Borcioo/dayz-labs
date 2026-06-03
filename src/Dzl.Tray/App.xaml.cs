using System.IO;
using System.Threading;
using System.Windows;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Ipc;
using Wpf.Ui.Appearance;

namespace Dzl.Tray;

/// <summary>
/// Single-instance WPF tray app. Starts hidden to the system tray, hosts the
/// named-pipe <see cref="PipeServer"/> so CLI/MCP actions route into this process,
/// and shows a status tray icon. No window is shown on launch.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleton;
    private CancellationTokenSource? _cts;
    private TrayIcon? _tray;

    /// <summary>
    /// Resolves the config path: <c>DZL_CONFIG</c> env var, else
    /// <c>%LOCALAPPDATA%\dzl\config.json</c>.
    /// </summary>
    public static string ConfigPath()
    {
        var env = Environment.GetEnvironmentVariable("DZL_CONFIG");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "dzl", "config.json");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply the dark Fluent theme app-wide (matches ThemesDictionary Theme="Dark").
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        // Single instance: bail out if another tray is already running.
        _singleton = new Mutex(initiallyOwned: true, "dzl-tray-singleton", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var configPath = ConfigPath();
        Profiles.EnsureDefault(configPath);

        // Host the IPC server in the background ONLY when automation is enabled; the tray
        // becomes the live authority for CLI/MCP. Off (default) = no background pipe listener.
        _cts = new CancellationTokenSource();
        var (cfg, _, _) = Profiles.ResolveActive(configPath);
        if (cfg.EnableAutomationServer)
            _ = new PipeServer(() => new LauncherService(configPath)).RunAsync(_cts.Token);

        _tray = new TrayIcon(configPath);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts?.Cancel();
        _tray?.Dispose();
        _cts?.Dispose();
        _singleton?.Dispose();
        base.OnExit(e);
    }
}
