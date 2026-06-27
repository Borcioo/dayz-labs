using System.Diagnostics;
using Dzl.Core.Config;
using Dzl.Core.Economy;
using Dzl.Core.Env;
using Dzl.Core.Procs;

namespace Dzl.Core.Launch;

public static class ProcessManager
{
    // Parse one line of `tasklist /FI "PID eq N" /NH /FO CSV`. First quoted
    // field is the image name. INFO:/empty -> null (not running).
    public static string? ParseImage(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith("INFO:", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = line.Split("\",\"");
        var first = parts[0].TrimStart('"').TrimEnd('"');
        return first.Length == 0 ? null : first;
    }

    /// <summary>Image name of a live PID via <c>tasklist</c>, or null when the PID is gone or the
    /// lookup fails. Never throws — this runs on every status call from every frontend.</summary>
    public static string? ImageOf(int pid)
    {
        var r = ProcRunner.Run("tasklist", new[] { "/FI", $"PID eq {pid}", "/NH", "/FO", "CSV" },
            new RunOpts(TimeoutMs: 10_000));
        return r.Ok ? ParseImage(r.StdOut) : null;
    }

    public static string ServerExe(DzlConfig c, string mode) => mode == "debug" ? c.ExeDebug : c.ExeNormal;
    public static string ClientExe(DzlConfig c, string mode) => mode == "debug" ? c.ClientExeDebug : c.ClientExeNormal;

    public static Process Spawn(string mode, string target, DzlConfig cfg, string source = "cli", string? configPath = null)
    {
        var exe = target == "server" ? ServerExe(cfg, mode) : ClientExe(cfg, mode);
        // Point the instance's serverDZ.cfg template at its own mission (absolute path) — the engine forces
        // $currentdir to the exe dir, so a bare template name would load the install's mission instead.
        if (target == "server" && MissionLocator.Resolve(cfg)?.MissionDir is { } missionDir)
            ServerScaffold.EnsureAbsoluteTemplate(cfg.ConfigName, missionDir);
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(cfg.DayzPath, exe),
            // Server with an absolute instance serverDZ.cfg runs from the instance dir so DayZ resolves
            // its -config + mission there (it rejects absolute -config). Client always from the install.
            WorkingDirectory = ArgvBuilder.WorkingDir(cfg, target),
            UseShellExecute = false,
        };
        foreach (var a in ArgvBuilder.Build(mode, target, cfg)) psi.ArgumentList.Add(a);
        var proc = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {exe}");
        if (configPath is not null) StateFile.Write(configPath, target, proc.Id, mode, source, exe);
        return proc;
    }

    public static void Stop(string target, DzlConfig cfg, string configPath)
    {
        var info = StateFile.ReadRaw(configPath).GetValueOrDefault(target);
        if (info is not null)
        {
            var img = ImageOf(info.Pid);
            if (img is not null && string.Equals(img, info.Exe, StringComparison.OrdinalIgnoreCase))
            {
                try { Process.GetProcessById(info.Pid).Kill(entireProcessTree: true); }
                catch (ArgumentException) { /* already gone */ }
                catch (InvalidOperationException) { /* already exited */ }
            }
        }
        StateFile.Clear(configPath, target);
    }

    public static Process Restart(string mode, DzlConfig cfg, string configPath, string source = "cli")
    {
        Stop("server", cfg, configPath);
        return Spawn(mode, "server", cfg, source, configPath);
    }
}
