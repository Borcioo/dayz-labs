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

            ProcessStartInfo psi;
            if (File.Exists(exePath))
            {
                psi = new ProcessStartInfo(exePath) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in MountArgs(source)) psi.ArgumentList.Add(a);
            }
            else if (!string.IsNullOrWhiteSpace(source))
            {
                psi = new ProcessStartInfo("subst") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add(drive);
                psi.ArgumentList.Add(source);
            }
            else
            {
                return false; // no exe and nothing to subst
            }

            using var p = Process.Start(psi);
            p?.WaitForExit(30000);   // bounded: mount shouldn't take long; never block forever
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
            ProcessStartInfo psi;
            if (File.Exists(exePath))
            {
                psi = new ProcessStartInfo(exePath) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/Dismount");
            }
            else
            {
                psi = new ProcessStartInfo("subst") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add(drive);
                psi.ArgumentList.Add("/D");
            }
            using var p = Process.Start(psi);
            p?.WaitForExit(30000);   // bounded: never freeze the caller
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
        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in ExtractArgs(gamePath, dest)) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return (false, "");
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            var output = string.Concat(stdout, stderr).Trim();
            return (p.ExitCode == 0, output);
        }
        catch
        {
            return (false, "");
        }
    }
}
