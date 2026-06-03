namespace Dzl.Core.Env;

public sealed record ScaffoldReport(
    bool CfgCreated, bool ProfilesCreated, bool ClientProfilesCreated, bool MissionCopied, string Notes);

public static class ServerScaffold
{
    /// <summary>A minimal, dev-friendly serverDZ.cfg (unsigned mods allowed, single mission).</summary>
    public static string DefaultServerCfg(string missionName = "dayzOffline.chernarusplus")
    {
        var map = missionName.Contains('.', StringComparison.Ordinal)
            ? missionName[(missionName.IndexOf('.', StringComparison.Ordinal) + 1)..]
            : "chernarusplus";
        return
$$"""
hostname = "dzl dev server";
password = "";
passwordAdmin = "";

maxPlayers = 60;

verifySignatures = 0;        // dev: allow unsigned mods
forceSameBuild = 0;

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
        template = "{{missionName}}.{{map}}";
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
                CopyDirectory(src, dst);
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

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
