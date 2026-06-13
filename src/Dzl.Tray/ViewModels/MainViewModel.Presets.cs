using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Config;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _activePreset = "";
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _selectedPreset = "";

    public ObservableCollection<string> Presets { get; } = new();

    /// <summary>Active instance name with a safe fallback (server instances can't be named "").</summary>
    private string ActiveName => string.IsNullOrEmpty(ActivePreset) ? "default" : ActivePreset;

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var p in Profiles.List(_configPath)) Presets.Add(p);
        SelectedPreset = ActivePreset;
    }

    // --- Presets -----------------------------------------------------------
    //
    // All preset ops run DIRECTLY against Profiles/Core (quick file I/O). The tray
    // process is the IPC authority, so routing through ControlPlane/PipeClient here
    // would block on a synchronous named-pipe round-trip to our OWN PipeServer and
    // deadlock the UI thread. Never reintroduce a ControlPlane call in these paths.

    /// <summary>Set true while <see cref="Reload"/> repopulates <see cref="Presets"/> so the
    /// ComboBox's SelectionChanged doesn't fire a re-switch during programmatic refresh.</summary>
    public bool SuppressPresetSwitch { get; private set; }

    [RelayCommand]
    private void SwitchPreset() => SwitchToPreset(SelectedPreset);

    [RelayCommand]
    private void SavePreset()
    {
        var name = string.IsNullOrWhiteSpace(NewPresetName) ? ActivePreset : NewPresetName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        // Make sure the live UI state (mods/order/mode) is in _cfg before snapshotting.
        Persist();
        Profiles.Save(_cfg, name, _configPath);
        Profiles.SetActive(name, _configPath);
        NewPresetName = "";
        Reload();
    }

    /// <summary>Switch to a named preset (used by the top-bar combo and menu items).</summary>
    public void SwitchToPreset(string? name)
    {
        if (SuppressPresetSwitch) return;
        if (string.IsNullOrEmpty(name) || name == ActivePreset) return;
        Profiles.SetActive(name, _configPath);
        Reload();
    }

    /// <summary>Delete a preset (the top-bar combo's current selection if none given), with
    /// confirmation. If it was active, clears the active marker so the default reseeds.</summary>
    [RelayCommand]
    private void DeletePreset(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? SelectedPreset : name.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var ok = System.Windows.MessageBox.Show(
            $"Delete preset \"{name}\"? This cannot be undone.",
            "Delete preset", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        Profiles.Delete(name, _configPath);
        if (ActivePreset == name) Profiles.SetActive("", _configPath);
        Reload();
    }
}
