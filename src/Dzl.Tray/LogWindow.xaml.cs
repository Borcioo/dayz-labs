using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>A single log pane popped out into its own window. Hosts a second <see cref="Controls.LogPaneView"/>
/// over the same <see cref="LogPaneVm"/> as the main page (so filter/search/auto-scroll stay in sync). The
/// window's DataContext is the host <see cref="MainViewModel"/> so the pane's command buttons resolve through
/// the FluentWindow ancestor exactly as they do in the main window. Opened ownerless on purpose: an owned
/// WPF-UI Mica window hides its owner when closed.</summary>
public partial class LogWindow : FluentWindow
{
    // Live detached windows keyed by pane, so a second detach focuses the existing window instead of duplicating.
    private static readonly Dictionary<LogPaneVm, LogWindow> Open = new();
    private readonly LogPaneVm _pane;

    private LogWindow(LogPaneVm pane, MainViewModel host)
    {
        InitializeComponent();
        _pane = pane;
        DataContext = host;            // command buttons in LogPaneView reach MainViewModel via the ancestor
        PaneHost.DataContext = pane;   // …while the view itself shows this pane's data
        SyncTitle();
        // Keep the caption current: FileName changes when logs re-resolve (server start / rollover / switch).
        pane.PropertyChanged += OnPaneChanged;
        Closed += (_, _) =>
        {
            _pane.PropertyChanged -= OnPaneChanged;
            Open.Remove(_pane);
            _pane.IsDetached = false;
        };
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogPaneVm.FileName) or nameof(LogPaneVm.Title)) SyncTitle();
    }

    private void SyncTitle() => Title = TitleBarCtl.Title = $"{_pane.Title} log — {_pane.FileName}";

    /// <summary>Pop <paramref name="pane"/> out into its own window, or focus the existing one.</summary>
    public static void Detach(LogPaneVm pane, MainViewModel host)
    {
        if (Open.TryGetValue(pane, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var w = new LogWindow(pane, host);
        Open[pane] = w;
        pane.IsDetached = true;
        w.Show();
    }

    /// <summary>Close the detached window for <paramref name="pane"/> (re-attaching it to the main page).</summary>
    public static void Reattach(LogPaneVm pane)
    {
        if (Open.TryGetValue(pane, out var w)) w.Close();
    }

    /// <summary>Close every detached log window (called when the main window closes).</summary>
    public static void CloseAll()
    {
        foreach (var w in Open.Values.ToArray()) w.Close();
    }
}
