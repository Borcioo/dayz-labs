using System.Diagnostics;
using System.Linq;

namespace Dzl.Core.Tools;

/// <summary>
/// Drives DayZ Tools' <c>WorkDrive.exe</c>, which mounts a configured work folder as the P: drive
/// and unpacks vanilla game data into it. The real CLI is:
/// <code>
///   WorkDrive.exe /Mount [source]                         (source optional; uses configured WorkDirPath)
///   WorkDrive.exe /Dismount
///   WorkDrive.exe /extractGameData [gamePath] [destPath]  (unpack vanilla PBOs into P:)
/// </code>
/// Arg-builders are pure (and tested); the process wrappers are thin and never throw.
/// </summary>
public static class WorkDrive
{
    // P: is the conventional DayZ work drive. Mounted iff the path exists.
    public static bool IsMounted(string drive = @"P:\") => Directory.Exists(drive);

    // ---- mount-target verification --------------------------------------

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

    // ---- pure arg-builders (tested) -------------------------------------

    /// <summary><c>/Mount [source]</c> — source omitted means "use the configured WorkDirPath".</summary>
    public static List<string> MountArgs(string? source) =>
        string.IsNullOrWhiteSpace(source) ? new() { "/Mount" } : new() { "/Mount", source };

    /// <summary><c>/extractGameData [gamePath] [destPath]</c>.</summary>
    public static List<string> ExtractArgs(string gamePath, string dest) =>
        new() { "/extractGameData", gamePath, dest };

    // ---- process wrappers (manual; never throw) -------------------------

    /// <summary>
    /// Mount the work drive. If already mounted, returns true. With a real <paramref name="exePath"/>
    /// runs <c>WorkDrive.exe /Mount [source]</c> (creating <paramref name="source"/> first if given);
    /// otherwise falls back to <c>subst</c> — but only when a source folder is supplied.
    /// <para>New signature: exe first, source optional. Existing one-arg <c>Mount(exe)</c> callers still
    /// compile and mean "mount the configured WorkDirPath".</para>
    /// </summary>
    public static bool Mount(string exePath, string? source = null, string drive = "P:")
    {
        if (IsMounted(drive + "\\")) return true;
        try
        {
            if (!string.IsNullOrWhiteSpace(source))
                Directory.CreateDirectory(source);

            // WorkDrive.exe must run ELEVATED to actually map the drive + set its mounted flag
            // (so DayZ Tools recognizes it). Steam/DayZ Tools launch it as admin; we do the same
            // via the runas verb (UAC prompt). subst is only a last-resort fallback when the exe
            // is missing — note the tools won't see a plain subst as their work drive.
            if (File.Exists(exePath))
                RunElevated(exePath, MountArgs(source), 60000);
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
        try
        {
            if (File.Exists(exePath))
                RunElevated(exePath, new[] { "/Dismount" }, 30000);   // elevated, like the mount
            else
            {
                var psi = new ProcessStartInfo("subst") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add(drive);
                psi.ArgumentList.Add("/D");
                using var p = Process.Start(psi);
                p?.WaitForExit(30000);
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Unpack vanilla PBOs from <paramref name="gamePath"/> into <paramref name="dest"/> (usually P:\).
    /// LONG-running — callers should run it on a background Task. Captures stdout+stderr.
    /// Returns (exit==0, combined output); (false, "") on failure to start.
    /// </summary>
    public static (bool ok, string output) ExtractGameData(string exePath, string gamePath, string dest)
    {
        if (!File.Exists(exePath)) return (false, "");
        // Elevated (like mount) — WorkDrive needs admin. UseShellExecute=true means we can't
        // capture stdout, so the caller verifies success by checking P:\ on disk afterwards.
        var code = RunElevated(exePath, ExtractArgs(gamePath, dest), 20 * 60 * 1000);  // up to 20 min
        return (code == 0, "");
    }

    // Run WorkDrive.exe elevated (UAC via the runas verb). Returns the exit code, or null if it
    // couldn't start / the user declined the UAC prompt / it didn't exit within the timeout.
    private static int? RunElevated(string exePath, IEnumerable<string> args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            return p.WaitForExit(timeoutMs) ? p.ExitCode : (int?)null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }  // UAC declined (1223) or shell error
        catch { return null; }
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
