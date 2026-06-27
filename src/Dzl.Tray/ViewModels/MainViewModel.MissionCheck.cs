using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Economy;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // Dashboard "Mission source" card: which mpmissions folder the SERVER will actually load (from the
    // active instance's serverDZ.cfg template) and whether that's the instance's mission or the install's.

    [ObservableProperty] private string _missionKind = "unknown";        // instance | install | missing | unknown
    [ObservableProperty] private string _missionStatusLabel = "—";
    [ObservableProperty] private string _missionEffectivePath = "";
    [ObservableProperty] private string _missionMessage = "";
    [ObservableProperty] private bool _missionFixVisible;

    private void RefreshMissionCheck()
    {
        var r = _svc.CheckMission();
        (MissionKind, MissionStatusLabel) = r.Status switch
        {
            MissionSourceStatus.Instance => ("instance", "Instance"),
            MissionSourceStatus.Install  => ("install", "Install"),
            MissionSourceStatus.Missing  => ("missing", "Missing"),
            _                            => ("unknown", "—"),
        };
        MissionEffectivePath = r.EffectivePath;
        MissionMessage = r.Message;
        MissionFixVisible = r.Fixable;
    }

    [RelayCommand]
    private void FixMissionTemplate() => RunOp(() =>
    {
        _svc.FixMissionTemplate();
        _dispatcher.Invoke(() => { RefreshMissionCheck(); RefreshPreview(); });
    });
}
