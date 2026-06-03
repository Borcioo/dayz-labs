using Dzl.Core.Config;
using Dzl.Core.Mods;
using Dzl.Core.Tools;

namespace Dzl.Core.Env;

public enum CheckSeverity { Error, Warning, Info }

public sealed record CheckItem(string Key, string Label, bool Ok, CheckSeverity Severity, string Detail);

public static class EnvCheck
{
    /// <summary>
    /// Diagnoses the local DayZ dev environment for a given config. Never throws:
    /// any per-check exception is reported as a failed item with the message in Detail.
    /// </summary>
    /// <param name="workdriveMounted">Stub for the P: check; defaults to the real WorkDrive.IsMounted().</param>
    /// <param name="toolsRegistered">Stub for the DayZ Tools registry check; defaults to EnvDetect.ToolsRegistered().</param>
    public static List<CheckItem> Run(DzlConfig cfg, Func<bool>? workdriveMounted = null, Func<bool>? toolsRegistered = null)
    {
        var items = new List<CheckItem>();
        Func<bool> isMounted = workdriveMounted ?? (() => WorkDrive.IsMounted());

        // 1. DayZ install
        items.Add(Check("dayz_install", "DayZ install", CheckSeverity.Error, () =>
        {
            var ok = Directory.Exists(cfg.DayzPath);
            return (ok, ok ? cfg.DayzPath : $"not found: {Show(cfg.DayzPath)}");
        }));

        // 2. Server exe (DayZDiag)
        items.Add(Check("server_exe", "Server exe (DayZDiag)", CheckSeverity.Error, () =>
        {
            var p = Path.Combine(cfg.DayzPath, cfg.ExeDebug);
            var ok = File.Exists(p);
            return (ok, ok ? p : $"not found: {Show(p)}");
        }));

        // 3. Client exe
        items.Add(Check("client_exe", "Client exe", CheckSeverity.Warning, () =>
        {
            var p = Path.Combine(cfg.DayzPath, cfg.ClientExeDebug);
            var ok = File.Exists(p);
            return (ok, ok ? p : $"not found: {Show(p)}");
        }));

        // 4. DayZ Tools (+ Workbench)
        items.Add(Check("dayz_tools", "DayZ Tools", CheckSeverity.Warning, () =>
        {
            var folderOk = Directory.Exists(cfg.DayzToolsPath);
            var wb = Path.Combine(cfg.DayzToolsPath, "Bin", "Workbench", "workbenchApp.exe");
            var wbOk = File.Exists(wb);
            if (folderOk && wbOk) return (true, wb);
            if (folderOk) return (false, $"folder exists but Workbench missing: {Show(wb)}");
            return (false, $"not found: {Show(cfg.DayzToolsPath)}");
        }));

        // 4b. DayZ Tools initialized (Steam install-script wrote the registry config)
        items.Add(Check("tools_registered", "DayZ Tools initialized", CheckSeverity.Warning, () =>
        {
            var ok = (toolsRegistered ?? EnvDetect.ToolsRegistered)();
            return (ok, ok
                ? "registry config present"
                : "Not initialized — open DayZ Tools once via Steam so its install setup runs (writes the registry).");
        }));

        // 5. P: work drive
        items.Add(Check("work_drive", "P: work drive", CheckSeverity.Warning, () =>
        {
            var ok = isMounted();
            return (ok, ok ? "P: mounted" : "P: not mounted");
        }));

        // 6. Game data extracted to P:
        items.Add(Check("game_data", "Game data extracted", CheckSeverity.Warning, () =>
        {
            if (!isMounted()) return (false, "mount P: first");
            var ok = Directory.Exists(@"P:\dz") || Directory.Exists(@"P:\DZ");
            return (ok, ok ? @"P:\dz" : @"vanilla data not extracted to P:\dz");
        }));

        // 7. serverDZ.cfg
        items.Add(Check("server_cfg", "serverDZ.cfg", CheckSeverity.Warning, () =>
        {
            var p = Path.IsPathRooted(cfg.ConfigName)
                ? cfg.ConfigName
                : Path.Combine(cfg.DayzPath, cfg.ConfigName);
            var ok = File.Exists(p);
            return (ok, ok ? p : $"not found: {Show(p)}");
        }));

        // 8. Profiles folders
        items.Add(Check("profiles", "Profiles folders", CheckSeverity.Warning, () =>
        {
            var sOk = Directory.Exists(cfg.ProfilesPath);
            var cOk = Directory.Exists(cfg.ClientProfilesPath);
            if (sOk && cOk) return (true, $"{cfg.ProfilesPath}; {cfg.ClientProfilesPath}");
            var missing = new List<string>();
            if (!sOk) missing.Add($"server: {Show(cfg.ProfilesPath)}");
            if (!cOk) missing.Add($"client: {Show(cfg.ClientProfilesPath)}");
            return (false, "missing " + string.Join("; ", missing));
        }));

        // 9. Mission
        items.Add(Check("mission", "Mission", CheckSeverity.Warning, () =>
        {
            var mission = StripLeadingDotSlash(cfg.Mission);
            var p = Path.IsPathRooted(mission) ? mission : Path.Combine(cfg.DayzPath, mission);
            var ok = Directory.Exists(p);
            return (ok, ok ? p : $"not found: {Show(p)}");
        }));

        // 10. Mods (info)
        items.Add(Check("mods", "Mods", CheckSeverity.Info, () =>
        {
            var n = ModDiscovery.Discover(cfg.ScanRoots).Count;
            return (n > 0, $"{n} mods found in scan-roots");
        }));

        return items;
    }

    private static CheckItem Check(string key, string label, CheckSeverity severity, Func<(bool ok, string detail)> probe)
    {
        try
        {
            var (ok, detail) = probe();
            return new CheckItem(key, label, ok, severity, detail);
        }
        catch (Exception ex)
        {
            return new CheckItem(key, label, false, severity, ex.Message);
        }
    }

    private static string StripLeadingDotSlash(string s)
    {
        if (s.StartsWith("./", StringComparison.Ordinal) || s.StartsWith(".\\", StringComparison.Ordinal))
            return s[2..];
        return s;
    }

    private static string Show(string path) => string.IsNullOrEmpty(path) ? "(empty)" : path;
}
