using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Bases;
using Dzl.Core.Servers;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // === Bases (templates) ================================================

    /// <summary>Sentinel for "use the DayZ install" in the New-server base dropdown.</summary>
    public const string VanillaChoice = "DayZ install (vanilla)";

    /// <summary>Discovered bases (cards on the Bases page).</summary>
    public ObservableCollection<BaseInfo> Bases { get; } = new();

    /// <summary>Base choices for the New-server dropdown: the vanilla sentinel + each base name.</summary>
    public ObservableCollection<string> BaseChoices { get; } = new();

    [RelayCommand]
    public void RefreshBases()
    {
        var list = ServerBases.List(ProjectsRoot);
        Bases.Clear();
        foreach (var b in list) Bases.Add(b);
        BaseChoices.Clear();
        BaseChoices.Add(VanillaChoice);
        foreach (var b in list) BaseChoices.Add(b.Name);
        OnPropertyChanged(nameof(ProjectsRoot));
    }

    public string CreateBaseFromInstall(string name, string map)
    {
        var (ok, msg) = ServerBases.CreateFromInstall(ProjectsRoot, name, _cfg.DayzPath, MapAliases.MissionTemplate(map));
        RefreshBases();
        return (ok ? "✓ " : "✗ ") + msg;
    }

    public string CreateEmptyBase(string name)
    {
        var (ok, msg) = ServerBases.CreateEmpty(ProjectsRoot, name);
        RefreshBases();
        return (ok ? "✓ " : "✗ ") + msg;
    }

    public string DeleteBase(string name)
    {
        var ok = ServerBases.Delete(ProjectsRoot, name);
        RefreshBases();
        return ok ? $"✓ deleted base '{name}'" : $"✗ no base '{name}'";
    }

    /// <summary>The folder of a base (for Open-folder).</summary>
    public string BaseDirOf(string name) => ServerBases.BaseDir(ProjectsRoot, name);
}
