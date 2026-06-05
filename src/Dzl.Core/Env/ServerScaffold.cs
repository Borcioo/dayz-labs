namespace Dzl.Core.Env;

public sealed record ScaffoldReport(
    bool CfgCreated, bool ProfilesCreated, bool ClientProfilesCreated, bool MissionCopied, string Notes);

public static class ServerScaffold
{
    /// <summary>A minimal, dev-friendly serverDZ.cfg (unsigned mods allowed, single mission).</summary>
    public static string DefaultServerCfg(string missionName = "dayzOffline.chernarusplus")
    {
        return
$$"""
hostname = "dzl dev server";
password = "";
passwordAdmin = "";

maxPlayers = 60;

verifySignatures = 0;        // dev: allow unsigned mods
forceSameBuild = 0;
allowFilePatching = 1;       // dev: accept clients launched with -filePatching (1 = with or without)

disableVoN = 0;
vonCodecQuality = 20;

instanceId = 1;
serverTimePersistent = 1;
storeHouseStateDisabled = false;
storageAutoFix = 1;

timeStampFormat = "Short";
logAverageFps = 1;
logMemory = 1;
logPlayers = 1;
logFile = "server_console.log";

class Missions
{
    class DayZ
    {
        template = "{{missionName}}";
    };
};
""";
    }

    /// <summary>
    /// Create instance dir, write serverDZ.cfg (only if absent), make profiles/profiles_client,
    /// and copy the mission from the DayZ install if present. Thin/manual; failures land in Notes.
    /// </summary>
    public static ScaffoldReport Scaffold(string dayzPath, string instanceDir,
        string missionName = "dayzOffline.chernarusplus")
    {
        bool cfgCreated = false, profilesCreated = false, clientProfilesCreated = false, missionCopied = false;
        var notes = new List<string>();

        try
        {
            Directory.CreateDirectory(instanceDir);

            var cfgPath = Path.Combine(instanceDir, "serverDZ.cfg");
            if (!File.Exists(cfgPath))
            {
                File.WriteAllText(cfgPath, DefaultServerCfg(missionName));
                cfgCreated = true;
            }
            else
            {
                notes.Add("serverDZ.cfg already exists; left untouched");
            }

            var profiles = Path.Combine(instanceDir, "profiles");
            if (!Directory.Exists(profiles)) { Directory.CreateDirectory(profiles); profilesCreated = true; }

            var clientProfiles = Path.Combine(instanceDir, "profiles_client");
            if (!Directory.Exists(clientProfiles)) { Directory.CreateDirectory(clientProfiles); clientProfilesCreated = true; }

            var src = Path.Combine(dayzPath, "mpmissions", missionName);
            var dst = Path.Combine(instanceDir, "mpmissions", missionName);
            if (Directory.Exists(src) && !Directory.Exists(dst))
            {
                CopyMission(src, dst);
                missionCopied = true;
            }
            else if (!Directory.Exists(src))
            {
                notes.Add($"mission source not found: {src}");
            }
            else
            {
                notes.Add("mission already present at target; not copied");
            }
        }
        catch (Exception ex)
        {
            notes.Add($"error: {ex.Message}");
        }

        return new ScaffoldReport(cfgCreated, profilesCreated, clientProfilesCreated, missionCopied,
            string.Join("; ", notes));
    }

    /// <summary>True for DayZ runtime/persistence folders a FRESH instance must NOT inherit from the
    /// install's mission — the engine regenerates them on first run. Currently the per-instance
    /// Central Economy storage dirs (<c>storage_&lt;instanceId&gt;</c>).</summary>
    public static bool IsRuntimeDir(string name) =>
        name.StartsWith("storage_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Delete the Central Economy persistence (<c>storage_*</c>) under this instance's
    /// missions so the next start regenerates it fresh. Returns how many storage folders were removed.
    /// Never throws.</summary>
    public static int WipePersistence(string instanceDir)
    {
        var n = 0;
        try
        {
            var mpm = Path.Combine(instanceDir, "mpmissions");
            if (!Directory.Exists(mpm)) return 0;
            foreach (var mission in Directory.EnumerateDirectories(mpm))
                foreach (var d in Directory.EnumerateDirectories(mission))
                    if (IsRuntimeDir(Path.GetFileName(d)))
                    { try { Directory.Delete(d, recursive: true); n++; } catch { /* skip locked */ } }
        }
        catch { /* best-effort */ }
        return n;
    }

    /// <summary>Recursively copy a mission folder, skipping live persistence (<c>storage_*</c>) so the
    /// copy starts clean. Used by instance scaffolding and by base/template creation.</summary>
    public static void CopyMission(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            if (IsRuntimeDir(Path.GetFileName(dir))) continue;   // don't copy live persistence
            CopyMission(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
    }

    /// <summary>Append <c>allowFilePatching = 1;</c> to a serverDZ.cfg if it's missing (dev clients
    /// launched with -filePatching can't connect without it). Best-effort; never throws.</summary>
    public static void EnsureFilePatching(string cfgPath)
    {
        try
        {
            if (!File.Exists(cfgPath)) return;
            var text = File.ReadAllText(cfgPath);
            if (!text.Contains("allowFilePatching", StringComparison.OrdinalIgnoreCase))
                File.AppendAllText(cfgPath, "\r\nallowFilePatching = 1;\r\n");
        }
        catch { /* best-effort */ }
    }
}
