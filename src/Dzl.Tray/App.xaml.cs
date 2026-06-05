using System.Linq;
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
    /// True when the named-pipe <see cref="PipeServer"/> was actually started this session
    /// (i.e. <c>EnableAutomationServer</c> was on at launch). Reflects the REAL running state,
    /// not just the config value (which only takes effect on next launch). Surfaced by the
    /// MainWindow "MCP" status pill.
    /// </summary>
    public static bool AutomationServerRunning { get; private set; }

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

        // Never hard-crash on an unhandled UI exception: log it + show it, keep running.
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash(ex.Exception);
            MessageBox.Show(ex.Exception.Message, "dzl — something went wrong",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception x) LogCrash(x);
        };

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

        // First run (no config file yet): walk the setup wizard before anything else.
        // The single-instance mutex is already held; a modal ShowDialog is safe here.
        // Whether the user finishes or cancels we then EnsureDefault so a config always exists.
        if (!File.Exists(configPath))
            new SetupWizardWindow(configPath).ShowDialog();

        Profiles.EnsureDefault(configPath);

        // Host the IPC server in the background ONLY when automation is enabled; the tray
        // becomes the live authority for CLI/MCP. Off (default) = no background pipe listener.
        _cts = new CancellationTokenSource();
        var (cfg, _, _) = Profiles.ResolveActive(configPath);
        if (cfg.EnableAutomationServer)
        {
            _ = new PipeServer(() => new LauncherService(configPath)).RunAsync(_cts.Token);
            AutomationServerRunning = true;
        }

        _tray = new TrayIcon(configPath);

        // Auto-mount the P: work drive on launch when opted in (background; idempotent — no-op if
        // already mounted). De-elevated, in this user session, so P: is visible to the tray + game.
        if (cfg.AutomountWorkDrive)
        {
            var toolsPath = cfg.DayzToolsPath;
            var source = Dzl.Core.Env.EnvDetect.WorkDriveSource(cfg.WorkDriveSource, toolsPath);
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var exe = Path.Combine(toolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
                    Dzl.Core.Tools.WorkDrive.Mount(File.Exists(exe) ? exe : "", source);
                }
                catch { /* best-effort; the status bar reflects the real state */ }
            });
        }

        // Show the main window on launch so it's visible immediately. Pass --tray (or --minimized)
        // to start hidden to the tray instead — used by login auto-start.
        bool startHidden = e.Args.Any(a =>
            a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
            _tray.ShowMainWindow();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath())!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}  {ex}\n\n");
        }
        catch { /* logging must never throw */ }
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
