using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dzl.Core.Tools;

/// <summary>Launches a child process at the NORMAL (non-elevated) user level even when the current
/// process is elevated — so things it mounts (the P: work drive) land in the user session the
/// game/Explorer see.</summary>
/// <remarks>Uses the elevated process's linked (limited) token + CreateProcessWithTokenW. Falls back
/// to a normal launch when not elevated or if anything fails (so it never regresses).</remarks>
public static class ProcessElevation
{
    /// <summary>Run exe+args, returning the exit code (null if it couldn't start / timed out).
    /// De-elevates to the user session when <paramref name="deElevateIfAdmin"/> and we're admin.</summary>
    public static int? Run(string exePath, IReadOnlyList<string> args, string workingDir, int timeoutMs,
                           bool deElevateIfAdmin, bool showWindow = false)
    {
        if (deElevateIfAdmin && OperatingSystem.IsWindows() && Dzl.Core.Env.EnvDetect.IsElevated())
        {
            var code = TryRunDeElevated(exePath, args, workingDir, timeoutMs, showWindow);
            if (code is not null) return code;   // success; else fall through to normal launch
        }
        return RunNormal(exePath, args, workingDir, timeoutMs, showWindow);
    }

    private static int? RunNormal(string exePath, IReadOnlyList<string> args, string workingDir, int timeoutMs, bool showWindow)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath)
            { UseShellExecute = false, CreateNoWindow = !showWindow, WorkingDirectory = workingDir };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            return p.WaitForExit(timeoutMs) ? p.ExitCode : (int?)null;
        }
        catch { return null; }
    }

    // Spawn the child via the elevated process's linked (limited) user token.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int? TryRunDeElevated(string exePath, IReadOnlyList<string> args, string workingDir, int timeoutMs, bool showWindow)
    {
        IntPtr hProc = GetCurrentProcess();
        IntPtr hToken = IntPtr.Zero, hLinked = IntPtr.Zero, hPrimary = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(hProc, TOKEN_QUERY | TOKEN_DUPLICATE, out hToken)) return null;

            // Get the linked (lower-integrity, normal-user) token.
            GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenLinkedToken, IntPtr.Zero, 0, out int len);
            if (len <= 0) return null;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenLinkedToken, buf, len, out len))
                    return null;
                hLinked = Marshal.ReadIntPtr(buf);  // TOKEN_LINKED_TOKEN.LinkedToken
            }
            finally { Marshal.FreeHGlobal(buf); }
            if (hLinked == IntPtr.Zero) return null;

            // CreateProcessWithTokenW needs a PRIMARY token; the linked one is impersonation-class.
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
            if (!DuplicateTokenEx(hLinked, TOKEN_ALL_ACCESS, ref sa,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out hPrimary))
                return null;

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            var cmdLine = BuildCommandLine(exePath, args);   // mutable buffer
            if (!CreateProcessWithTokenW(hPrimary, LOGON_WITH_PROFILE, exePath, cmdLine,
                    showWindow ? 0u : CREATE_NO_WINDOW, IntPtr.Zero, workingDir, ref si, out var pi))
                return null;

            try
            {
                uint wait = WaitForSingleObject(pi.hProcess, (uint)timeoutMs);
                if (wait != 0 /*WAIT_OBJECT_0*/) return null;
                return GetExitCodeProcess(pi.hProcess, out uint exit) ? (int)exit : (int?)null;
            }
            finally
            {
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            }
        }
        catch { return null; }
        finally
        {
            if (hToken != IntPtr.Zero) CloseHandle(hToken);
            if (hLinked != IntPtr.Zero) CloseHandle(hLinked);
            if (hPrimary != IntPtr.Zero) CloseHandle(hPrimary);
        }
    }

    // CreateProcessWithTokenW wants a single command-line string; quote the exe + args.
    // Public + pure so the quoting rules are unit-testable (the rest of this class needs a real
    // elevated token).
    public static string BuildCommandLine(string exePath, IReadOnlyList<string> args)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('"').Append(exePath).Append('"');
        foreach (var a in args)
        {
            sb.Append(' ');
            AppendArgument(sb, a);
        }
        return sb.ToString();
    }

    /// <summary>MSVCRT argv quoting: backslashes are literal except when they precede a quote (or
    /// the closing quote), where each must be doubled. The naive Replace("\"","\\\"") variant broke
    /// args ending in a backslash — <c>"C:\DayZ Projects\"</c> parses as an escaped quote and
    /// swallows the next argument.</summary>
    private static void AppendArgument(System.Text.StringBuilder sb, string arg)
    {
        if (arg.Length != 0 && !arg.Any(c => c is ' ' or '\t' or '"'))
        {
            sb.Append(arg);
            return;
        }
        sb.Append('"');
        var i = 0;
        while (i < arg.Length)
        {
            var backslashes = 0;
            while (i < arg.Length && arg[i] == '\\') { backslashes++; i++; }
            if (i == arg.Length)
            {
                sb.Append('\\', backslashes * 2);   // before the closing quote: double them
            }
            else if (arg[i] == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
                i++;
            }
            else
            {
                sb.Append('\\', backslashes).Append(arg[i]);
                i++;
            }
        }
        sb.Append('"');
    }

    private const uint TOKEN_QUERY = 0x0008, TOKEN_DUPLICATE = 0x0002, TOKEN_ALL_ACCESS = 0xF01FF;
    private const uint LOGON_WITH_PROFILE = 0x00000001, CREATE_NO_WINDOW = 0x08000000;

    private enum TOKEN_INFORMATION_CLASS { TokenLinkedToken = 19 }
    private enum SECURITY_IMPERSONATION_LEVEL { SecurityImpersonation = 2 }
    private enum TOKEN_TYPE { TokenPrimary = 1 }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, TOKEN_INFORMATION_CLASS cls, IntPtr info, int len, out int retLen);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, ref SECURITY_ATTRIBUTES attrs, SECURITY_IMPERSONATION_LEVEL level, TOKEN_TYPE type, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? appName, string cmdLine, uint creationFlags, IntPtr env, string? cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr h);
}
