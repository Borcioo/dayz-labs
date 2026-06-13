using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dzl.Core.Config;
using Dzl.Core.Ipc;
using Dzl.Core.Launch;
using Dzl.Core.Env;
using Dzl.Core.Tools;
using H.NotifyIcon;

namespace Dzl.Tray;

/// <summary>
/// Wraps an H.NotifyIcon <see cref="TaskbarIcon"/>: a context menu wired to
/// <see cref="ControlPlane"/> and a 1.5s status poll that recolours the icon and
/// tooltip based on whether the server is live in the launcher state file.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly string _configPath;
    private readonly string _configDir;
    private readonly TaskbarIcon _icon;
    private readonly DispatcherTimer _timer;

    private static readonly Color UpColor = Color.FromArgb(76, 175, 80);     // green
    private static readonly Color DownColor = Color.FromArgb(120, 120, 120); // grey

    // The status dot is regenerated on every state flip rather than swapping two cached
    // Icon instances: H.NotifyIcon disposes the previous Icon when the property changes,
    // so reusing a cached one would hand it a disposed handle on the next swap. We own the
    // current Icon's lifetime and dispose the prior one ourselves after each assignment.
    private Icon? _currentIcon;

    private MainWindow? _window;
    private bool _lastUp;
    private bool _disposed;

    public TrayIcon(string configPath)
    {
        _configPath = configPath;
        _configDir = Path.GetDirectoryName(configPath) ?? ".";

        _currentIcon = MakeDot(DownColor);
        _icon = new TaskbarIcon
        {
            ToolTipText = "DayZ Labs — server down",
            Icon = _currentIcon,
            ContextMenu = BuildMenu(),
        };
        _icon.ForceCreate();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    // Guards the poll: StateFile.ReadLive spawns a tasklist per recorded PID and can outlast the
    // 1.5s tick — without this a slow read would stack up behind the next tick.
    private bool _polling;

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItem("Start server (debug)", StartServer));
        menu.Items.Add(MenuItem("Stop server", StopServer));
        menu.Items.Add(MenuItem("Restart server", RestartServer));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildToolsMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Open main window", OpenMainWindow));
        menu.Items.Add(MenuItem("Open config folder", OpenConfigFolder));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Quit", Quit));

        return menu;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    // Builds the "Tools ▸" submenu from the discovered DayZ Tools catalog.
    private MenuItem BuildToolsMenu()
    {
        var sub = new MenuItem { Header = "Tools" };
        string toolsPath;
        try { toolsPath = Profiles.ResolveActive(_configPath).cfg.DayzToolsPath; }
        catch { toolsPath = ""; }

        var catalog = string.IsNullOrWhiteSpace(toolsPath)
            ? new List<ToolEntry>()
            : ToolCatalog.Discover(toolsPath);

        var present = catalog
            .Where(t => t.Exists && t.Kind == ToolKind.LaunchOnly)
            .ToList();

        if (present.Count == 0)
        {
            sub.Items.Add(new MenuItem { Header = "(no tools found)", IsEnabled = false });
        }
        else
        {
            foreach (var tool in present)
            {
                var item = new MenuItem { Header = tool.DisplayName };
                var captured = tool;
                item.Click += (_, _) => Task.Run(() => ToolLauncher.Launch(captured));
                sub.Items.Add(item);
            }
        }

        sub.Items.Add(new Separator());
        var mounted = WorkDrive.IsMounted();
        var wd = new MenuItem { Header = $"Work drive: P: {(mounted ? "✓" : "✗")}" };
        wd.Click += (_, _) =>
        {
            if (WorkDrive.IsMounted()) return;
            Task.Run(() =>
            {
                var wdExe = Path.Combine(toolsPath, "Bin", "WorkDrive", "WorkDrive.exe");
                WorkDrive.Mount(File.Exists(wdExe) ? wdExe : "", EnvDetect.WorkDir(toolsPath));
            });
        };
        sub.Items.Add(wd);

        return sub;
    }

    // --- Server ops: run off the UI thread, report via balloon. ---

    private void StartServer(object sender, RoutedEventArgs e) =>
        RunOp(() => new ControlPlane(_configPath).StartJson("debug", false, "tui"));

    private void StopServer(object sender, RoutedEventArgs e) =>
        RunOp(() => new ControlPlane(_configPath).StopJson(false));

    private void RestartServer(object sender, RoutedEventArgs e) =>
        RunOp(() => new ControlPlane(_configPath).RestartJson("debug", "tui"));

    private void RunOp(Func<string> op)
    {
        Task.Run(() =>
        {
            string msg;
            try { msg = op(); }
            catch (Exception ex) { msg = "error: " + ex.Message; }
            _icon.Dispatcher.BeginInvoke(() =>
                _icon.ShowNotification("dzl", msg));
        });
    }

    private void OpenMainWindow(object sender, RoutedEventArgs e) => ShowMainWindow();

    /// <summary>Show (create if needed) and focus the single main window. Reused by the tray
    /// menu and by startup so the window can be visible immediately.</summary>
    public void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OpenConfigFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_configDir);
            ShellOpen.Folder(_configDir);
        }
        catch (Exception ex)
        {
            _icon.ShowNotification("dzl", "could not open folder: " + ex.Message);
        }
    }

    private void Quit(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    // --- Status poll ---

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        bool up;
        try
        {
            // ReadLive validates each PID via tasklist (process spawn) — keep it off the UI thread.
            var live = await Task.Run(() => StateFile.ReadLive(_configPath, ProcessManager.ImageOf));
            up = live.ContainsKey("server");
        }
        catch
        {
            up = false;
        }
        finally { _polling = false; }

        if (_disposed) return;
        if (up == _lastUp && _icon.Icon is not null) return;
        _lastUp = up;
        _icon.ToolTipText = up ? "DayZ Labs — server UP" : "DayZ Labs — server down";

        // Fresh icon each flip; dispose the one we previously created after the swap.
        var next = MakeDot(up ? UpColor : DownColor);
        var prev = _currentIcon;
        _icon.Icon = next;
        _currentIcon = next;
        prev?.Dispose();
    }

    /// <summary>Generates a tiny 16x16 status-dot icon at runtime (no .ico asset needed).</summary>
    private static Icon MakeDot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        var hicon = bmp.GetHicon();
        // Clone so we own a managed copy independent of the GDI handle.
        using var tmp = Icon.FromHandle(hicon);
        var icon = (Icon)tmp.Clone();
        NativeDestroyIcon(hicon);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    private static extern bool NativeDestroyIcon(IntPtr handle);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
