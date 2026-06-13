using System.Collections.ObjectModel;
using System.IO;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Projects;
using Dzl.Core.Servers;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // === Servers (instances) page =========================================

    /// <summary>Server instances discovered under &lt;ProjectsRoot&gt;\servers (drives the Servers page).</summary>
    public ObservableCollection<ServerInstance> Servers { get; } = new();

    /// <summary>Map choices for the New server form (aliases the Core resolves to mission templates).</summary>
    public static IReadOnlyList<string> Maps { get; } = new[] { "chernarus", "livonia", "sakhal" };

    public void RefreshServers()
    {
        Servers.Clear();
        foreach (var s in new ServerService(_configPath).List()) Servers.Add(s);
        OnPropertyChanged(nameof(ProjectsRoot));
    }

    /// <summary>Create a new server instance (scaffold + atomic preset) and reload so it's active.
    /// <paramref name="baseName"/> = a base/template to copy from, or null for the DayZ install.
    /// Returns a status line.</summary>
    public async Task<string> CreateServerAsync(string name, string map, int? port, string? baseName = null)
    {
        var cp = _configPath;
        // The mission-template copy can be hundreds of MB — run it off the UI thread; the
        // active-preset reload + server-list refresh hop back afterward.
        var res = await Task.Run(() => new ServerService(cp).Create(name, map, port, activate: true, baseName: baseName));
        Reload();              // active preset changed → refresh mods/paths/preset list
        RefreshServers();
        return res.Ok ? $"✓ {res.Message}  (port {res.Port})" : $"✗ {res.Message}";
    }

    /// <summary>Switch the active preset to a server instance's preset (by name).</summary>
    public string UseServer(string name)
    {
        var res = SetPresetByName(name);
        return res ? $"✓ active server → {name}" : $"✗ no preset '{name}'";
    }

    /// <summary>Activate a preset by name + reload; false if it doesn't exist.</summary>
    private bool SetPresetByName(string name)
    {
        if (!Profiles.List(_configPath).Contains(name)) return false;
        Profiles.SetActive(name, _configPath);
        Reload();
        return true;
    }

    /// <summary>Persist edited per-server settings to the active instance, then reload.</summary>
    public void SaveActiveInstance(DzlConfig edited)
    {
        Profiles.Save(edited, ActiveName, _configPath);
        Reload();
        RefreshServers();
    }

    /// <summary>Delete a server instance. If it was active, fall back to another (or a fresh default).</summary>
    public string DeleteServer(string name, bool removeFiles = false)
    {
        var wasActive = ActivePreset == name;
        if (!Profiles.Delete(name, _configPath, removeFiles)) return $"✗ no server '{name}'";
        if (wasActive)
        {
            // Fall back to another instance so something stays active (else seed a fresh default).
            var remaining = Profiles.List(_configPath);
            if (remaining.Count > 0) Profiles.SetActive(remaining[0], _configPath);
            else { Profiles.SetActive("", _configPath); Profiles.EnsureDefault(_configPath); }
        }
        Reload();
        RefreshServers();
        return removeFiles ? $"✓ deleted '{name}' + its files" : $"✓ deleted '{name}' (files kept on disk)";
    }

    /// <summary>Clone the active instance's config to a new name and activate it.</summary>
    public string CloneActive(string newName)
    {
        if (!ProjectPaths.IsValidName(newName)) return $"✗ invalid name: {newName}";
        if (Profiles.List(_configPath).Contains(newName)) return $"✗ '{newName}' already exists";
        Profiles.Save(_cfg, newName, _configPath);    // _cfg = the active composed config
        Profiles.SetActive(newName, _configPath);
        Reload();
        RefreshServers();
        return $"✓ cloned active → '{newName}' (now active)";
    }

    /// <summary>The active server's folder (where its serverDZ.cfg / mpmissions / profiles live).</summary>
    public string ActiveServerDir =>
        Path.IsPathRooted(_cfg.ConfigName)
            ? Path.GetDirectoryName(_cfg.ConfigName) ?? ProjectPaths.ServerDir(ProjectsRoot, ActiveName)
            : ProjectPaths.ServerDir(ProjectsRoot, ActiveName);

    /// <summary>Delete the active server's Central Economy persistence (storage_*) so the next start
    /// regenerates it fresh. Returns a status line.</summary>
    public string WipeActivePersistence() => WipePersistenceDir(ActiveServerDir);

    /// <summary>Wipe persistence (storage_*) for the server whose files live in <paramref name="dir"/>.</summary>
    public string WipePersistenceDir(string dir)
    {
        var n = ServerScaffold.WipePersistence(dir);
        return n > 0
            ? $"✓ wiped {n} storage folder(s) — fresh persistence on next start"
            : "nothing to wipe (persistence is already clean)";
    }

    /// <summary>Rename the active instance (copy → new name, delete old, activate new).</summary>
    public string RenameActive(string newName)
    {
        var old = ActiveName;
        if (!ProjectPaths.IsValidName(newName)) return $"✗ invalid name: {newName}";
        if (string.Equals(newName, old, StringComparison.OrdinalIgnoreCase)) return "✗ same name";
        if (Profiles.List(_configPath).Contains(newName)) return $"✗ '{newName}' already exists";
        Profiles.Save(_cfg, newName, _configPath);
        Profiles.Delete(old, _configPath);
        Profiles.SetActive(newName, _configPath);
        Reload();
        RefreshServers();
        return $"✓ renamed '{old}' → '{newName}'";
    }
}
