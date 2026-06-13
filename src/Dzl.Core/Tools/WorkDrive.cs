using System.Diagnostics;

namespace Dzl.Core.Tools;

/// <summary>Drives DayZ Tools' <c>WorkDrive.exe</c>, which mounts a configured work folder as the
/// P: drive and unpacks vanilla game data into it.</summary>
/// <remarks>The real CLI is:
/// <code>
///   WorkDrive.exe /Mount [source]                         (source optional; uses configured WorkDirPath)
///   WorkDrive.exe /Dismount
///   WorkDrive.exe /extractGameData [gamePath] [destPath]  (unpack vanilla PBOs into P:)
/// </code>
/// Arg-builders are pure (and tested); the process wrappers are thin and never throw.</remarks>
public static class WorkDrive
{
    // P: is the conventional DayZ work drive. "Mounted" means a real, accessible drive in THIS
    // process's session — DriveInfo.IsReady, not just Directory.Exists (which can follow an orphan
    // DOS-device mapping left in another session/elevation level that isn't actually usable here).
    public static bool IsMounted(string drive = @"P:\")
    {
        try
        {
            var letter = drive.TrimEnd('\\', ':');
            return new DriveInfo(letter).IsReady;
        }
        catch { return Directory.Exists(drive); }
    }

    // Strip the NT object-manager prefix from a QueryDosDevice result: "\??\D:\DayZWorkDrive" -> "D:\DayZWorkDrive".
    // Returns null for empty/whitespace. Pure — TESTED.
    public static string? ParseDosDeviceTarget(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith(@"\??\")) s = s.Substring(4);
        return s.Length == 0 ? null : s;
    }

    // The real filesystem path a drive letter maps to (subst/mount), or null if unmapped.
    // Uses QueryDosDevice. Windows-only / manual (not unit-tested).
    public static string? MountTarget(string drive = "P:")
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            var name = drive.TrimEnd('\\', ':') + ":";   // normalize to "P:"
            var buf = new char[1024];
            uint n = QueryDosDevice(name, buf, (uint)buf.Length);
            if (n == 0) return null;
            // QueryDosDevice returns a double-null-terminated list; take the first entry.
            var raw = new string(buf, 0, (int)n).Split('\0').FirstOrDefault(x => x.Length > 0);
            return ParseDosDeviceTarget(raw);
        }
        catch { return null; }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string lpDeviceName, char[] lpTargetPath, uint ucchMax);

    // True if two paths point at the same folder (case-insensitive, trailing-slash-insensitive). Pure — TESTED.
    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            string N(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
            return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Exact arg shape DayZ Tools itself uses (from WorkDrive's own log):
    //   /y /Silent /nowarnings /mount P: "<source>"
    //   /y /Silent /nowarnings /dismount P:
    //   /y /Silent /nowarnings /extractGameData "<game>" "<dest>"
    // The drive LETTER ("P:") is required after /mount — omitting it (our old bug) made WorkDrive
    // treat the source path as the drive letter and silently fail. /y /Silent /nowarnings suppress
    // all confirmation prompts (so nothing blocks/hangs).
    private static readonly string[] Silent = { "/y", "/Silent", "/nowarnings" };
    private static string DriveColon(string drive) => drive.TrimEnd('\\', ':') + ":";

    /// <summary><c>/mount P: [source]</c> — drive letter required; source = the folder to map.</summary>
    public static List<string> MountArgs(string? source, string drive = "P")
    {
        var a = new List<string>(Silent) { "/mount", DriveColon(drive) };
        if (!string.IsNullOrWhiteSpace(source)) a.Add(source);
        return a;
    }

    /// <summary><c>/dismount P:</c>.</summary>
    public static List<string> DismountArgs(string drive = "P") =>
        new(Silent) { "/dismount", DriveColon(drive) };

    /// <summary><c>/ExtractGameData</c> with NO paths — exactly what DayZ Tools itself runs; it reads
    /// the game path + work-drive letter from settings.ini and unpacks vanilla PBOs to P:\ (under their
    /// PBO prefixes, e.g. P:\DZ\...). Passing an explicit dest caused property-file write errors.</summary>
    // Extract drops /Silent (keeps /y /nowarnings: auto-confirm, no warning prompts) so WorkDrive
    // shows its own progress console — extract is long and the user wants to see it working.
    public static List<string> ExtractArgs() => new() { "/y", "/nowarnings", "/ExtractGameData" };

    /// <summary>Mount the work drive (returns true if already mounted). With a real
    /// <paramref name="exePath"/> runs <c>WorkDrive.exe /Mount [source]</c> (creating
    /// <paramref name="source"/> first if given); otherwise falls back to <c>subst</c>, but only when
    /// a source folder is supplied.</summary>
    public static bool Mount(string exePath, string? source = null, string drive = "P:")
    {
        if (IsMounted(drive + "\\")) return true;
        try
        {
            if (!string.IsNullOrWhiteSpace(source))
                Directory.CreateDirectory(source);

            // Run WorkDrive.exe /Mount [source] in the user session so the resulting P: is visible
            // here and in Explorer (NOT runas — that mounts in a separate admin session). subst is
            // a last-resort fallback when the exe is missing (DayZ Tools won't see a plain subst).
            if (File.Exists(exePath))
                RunWorkDrive(exePath, MountArgs(source, drive), 60000);
            else if (!string.IsNullOrWhiteSpace(source))
                RunSubst(drive, source);
            else
                return false;
        }
        catch
        {
            return false;
        }
        return IsMounted(drive + "\\");
    }

    /// <summary>Dismount via <c>WorkDrive.exe /Dismount</c> if present, else <c>subst {drive} /D</c>.</summary>
    public static void Unmount(string exePath, string drive = "P:")
    {
        // 1) DayZ Tools' own dismount (elevated), if the exe is there.
        try { if (File.Exists(exePath)) RunWorkDrive(exePath, DismountArgs(drive), 30000); }
        catch { /* best-effort */ }
        // 2) ALWAYS also clear a plain subst — an earlier (pre-elevation) fallback may have left a
        //    `subst P: <dir>` that WorkDrive /Dismount doesn't remove and that DayZ Tools ignores.
        try { RunSubstDelete(drive); } catch { /* best-effort */ }
    }

    private static void RunSubstDelete(string drive)
    {
        if (!Directory.Exists(drive.TrimEnd(':') + ":\\")) return;   // nothing mapped
        var psi = new ProcessStartInfo("subst") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add(drive.TrimEnd('\\', ':') + ":");
        psi.ArgumentList.Add("/D");
        using var p = Process.Start(psi);
        p?.WaitForExit(10000);
    }

    /// <summary>Run DayZ Tools' game-data extraction (vanilla PBOs → P:\, using settings.ini's game
    /// path + work-drive letter). Returns (exit==0, ""); (false, "") on failure to start.</summary>
    /// <remarks>Runs SYNCHRONOUSLY: WorkDrive unpacks every out-of-date PBO and then exits 0 ("The
    /// tasks were executed."). Also IDEMPOTENT — re-running version-checks each PBO ("Data seems to be
    /// up to date") and exits in ~1s, so the same call doubles as a verify. <paramref name="showWindow"/>
    /// shows WorkDrive's progress console (true for a real extract to watch; false for a quiet
    /// re-check).</remarks>
    public static (bool ok, string output) ExtractGameData(string exePath, bool showWindow = true)
    {
        if (!File.Exists(exePath)) return (false, "");
        var code = RunWorkDrive(exePath, ExtractArgs(), 20 * 60 * 1000, showWindow);  // up to 20 min
        return (code == 0, "");
    }

    // Run WorkDrive.exe IN THE SAME session (no runas). WorkDrive has no requireAdministrator
    // manifest, so a normal launch maps P: into the user session where Explorer + this app can see
    // it. Launching it elevated (runas) mounts P: in a separate admin session that the user session
    // can't see — which is exactly the "shows mounted but no P:" bug. Returns the exit code, or null
    // if it couldn't start / didn't exit within the timeout.
    private static int? RunWorkDrive(string exePath, IEnumerable<string> args, int timeoutMs, bool showWindow = false)
    {
        return ProcessElevation.Run(exePath, args is IReadOnlyList<string> l ? l : new List<string>(args),
                                    Path.GetDirectoryName(exePath) ?? "", timeoutMs, deElevateIfAdmin: true, showWindow);
    }

    private static void RunSubst(string drive, string source)
    {
        var psi = new ProcessStartInfo("subst") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add(drive);
        psi.ArgumentList.Add(source);
        using var p = Process.Start(psi);
        p?.WaitForExit(30000);
    }
}
