using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Tools;
using Dzl.Core.Vcs;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // --- Params / Config dialogs (called from MainWindow menu handlers) ----

    /// <summary>Current per-target/per-mode params list for the given target.</summary>
    public List<string> CurrentParams(string target, string mode) =>
        (target, mode) switch
        {
            ("server", "normal") => _cfg.ServerParamsNormal,
            ("server", _) => _cfg.ServerParamsDebug,
            ("client", "normal") => _cfg.ClientParamsNormal,
            _ => _cfg.ClientParamsDebug,
        };

    /// <summary>Defaults for the given target/mode params list (for the Reset button).</summary>
    public static List<string> DefaultParams(string target, string mode)
    {
        var d = DzlConfig.Default();
        return (target, mode) switch
        {
            ("server", "normal") => d.ServerParamsNormal,
            ("server", _) => d.ServerParamsDebug,
            ("client", "normal") => d.ClientParamsNormal,
            _ => d.ClientParamsDebug,
        };
    }

    /// <summary>Apply an edited params list to the matching cfg slot, save, refresh preview.</summary>
    public void ApplyParams(string target, string mode, List<string> values)
    {
        _cfg = (target, mode) switch
        {
            ("server", "normal") => _cfg with { ServerParamsNormal = values },
            ("server", _) => _cfg with { ServerParamsDebug = values },
            ("client", "normal") => _cfg with { ClientParamsNormal = values },
            _ => _cfg with { ClientParamsDebug = values },
        };
        Profiles.Save(_cfg, ActiveName, _configPath);   // launch params are per-server
        RefreshPreview();
    }

    /// <summary>Apply an edited config (from the Settings dialog): save then full reload.</summary>
    public void ApplyConfig(DzlConfig edited)
    {
        // Settings edits global fields; persist both slices so per-server values (still shown on the
        // Settings page for now) also survive. Globals → config.json, per-server → active instance.
        GlobalStore.Save(edited.GlobalPart(ActiveName), _configPath);
        Profiles.Save(edited, ActiveName, _configPath);
        Reload();
    }

    /// <summary>Effective work-drive source for display in Settings (or a hint when not resolvable).</summary>
    public string ResolvedWorkDriveSource => WorkDriveSource ?? "(not detected — set the DayZ Tools path)";

    /// <summary>Effective keys folder shown in Settings — the override or the <c>&lt;ProjectsRoot&gt;\keys</c> default.</summary>
    public string ResolvedKeysDir => ProjectPaths.KeysDir(ProjectsRoot, _cfg.KeysDir);

    /// <summary>Effective signing-key name shown in Settings — the config value, else the cached author.</summary>
    public string ResolvedSigningKey
    {
        get
        {
            var n = !string.IsNullOrWhiteSpace(_cfg.SigningKey) ? _cfg.SigningKey.Trim() : CachedAuthor;
            return string.IsNullOrWhiteSpace(n) ? "(none — set a name or author)" : n;
        }
    }

    // === Code editor ======================================================

    /// <summary>True when a code editor is configured (drives the "Open in editor" buttons).</summary>
    public bool HasEditor => !string.IsNullOrWhiteSpace(_cfg.EditorPath);

    /// <summary>Open a folder (mod project or server instance) in the configured editor. Returns a status.</summary>
    public string OpenInEditor(string folder)
    {
        if (!HasEditor) return "✗ no editor set — Settings → Editor → Detect";
        return EditorLauncher.Open(_cfg.EditorPath, folder)
            ? $"✓ opened {Path.GetFileName(folder.TrimEnd('\\', '/'))} in editor"
            : "✗ could not launch the editor";
    }

    /// <summary>Detected editors on this machine (for the Settings Detect button).</summary>
    public List<EditorInfo> DetectEditors() => EditorDetect.Detect();

    /// <summary>GitHub account label for the Settings → Accounts row (keyless; reflects gh's OAuth login).</summary>
    [ObservableProperty] private string _ghAccount = "checking…";

    /// <summary>Whether gh reports a logged-in account (drives Login/Logout button enablement).</summary>
    [ObservableProperty] private bool _ghLoggedIn;

    /// <summary>Refresh the GitHub auth label off the UI thread (gh auth status shells out).</summary>
    public async Task RefreshGitHubAuthAsync()
    {
        var a = await Task.Run(() => GitHub.AuthStatus());
        GhLoggedIn = a.LoggedIn;
        GhAccount = a.Detail;
        GitHubReady = a.LoggedIn;   // drives the "From GitHub" tab availability (same status read)
    }

    /// <summary>Log out of GitHub (gh auth logout), then refresh the label.</summary>
    public async Task GitHubLogoutAsync()
    {
        await Task.Run(() => GitHub.Logout());
        await RefreshGitHubAuthAsync();
    }
}
