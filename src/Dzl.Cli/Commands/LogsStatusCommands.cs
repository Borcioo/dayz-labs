using System.CommandLine;
using System.Text.Json;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Logs;

namespace Dzl.Cli.Commands;

/// <summary>logs / status.</summary>
internal static class LogsStatusCommands
{
    public static Command Logs(CliContext c)
    {
        var logsWhich = new Argument<string>("which", "script|rpt|adm|client")
            .FromAmong("script", "rpt", "adm", "client");
        var logsLines = new Option<int?>("--lines", "Print the last N lines and exit.");
        var logsDiagnose = new Option<bool>("--diagnose",
            "Scan the tail for known DayZ verification-kick codes (VE_MISSING_BISIGN etc.) and explain them.");
        var logsCmd = new Command("logs", "Resolve a log path (or print last N lines with --lines).")
        { logsWhich, logsLines, logsDiagnose };
        logsCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, _) = c.Resolve(ctx);
            var which = ctx.ParseResult.GetValueForArgument(logsWhich);
            var lines = ctx.ParseResult.GetValueForOption(logsLines);
            var diagnose = ctx.ParseResult.GetValueForOption(logsDiagnose);
            var path = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath).GetValueOrDefault(which);
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine($"no {which} log found");
                return;
            }
            if (diagnose)
            {
                var tail = string.Join('\n', LogTail.LastLines(path, lines ?? 500));
                var diags = Dzl.Core.Build.BuildDiagnostics.DiagnoseAll(tail);
                Console.WriteLine(diags.Count > 0
                    ? Dzl.Core.Build.BuildDiagnostics.Format(diags)
                    : "no known kick/verification or build-tool signatures in the tail");
                return;
            }
            if (lines is int n)
            {
                foreach (var line in LogTail.LastLines(path, n)) Console.WriteLine(line);
                return;
            }
            Console.WriteLine(path);
        });
        return logsCmd;
    }

    public static Command Status(CliContext c)
    {
        var statusJson = new Option<bool>("--json", "Print machine-readable JSON.");
        var statusCmd = new Command("status", "Show launcher status.") { statusJson };
        statusCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var report = new LauncherService(configPath).Status();

            if (ctx.ParseResult.GetValueForOption(statusJson))
            {
                Console.WriteLine(JsonSerializer.Serialize(report, ConfigStore.Json));
                return;
            }

            string Line(TargetState t) => t.State == "down"
                ? "down"
                : $"up (pid {t.Pid}, {t.Mode}, src {t.Source})";

            Console.WriteLine($"mode:          {report.Mode}");
            Console.WriteLine($"port:          {report.Port}");
            Console.WriteLine($"active server: {report.ActivePreset ?? "(none)"}");
            Console.WriteLine($"server:        {Line(report.Server)}");
            Console.WriteLine($"client:        {Line(report.Client)}");
            Console.WriteLine($"dayz:          {report.Paths.GetValueOrDefault("dayz_path")}");
            Console.WriteLine($"profiles:      {report.Paths.GetValueOrDefault("profiles_path")}");
            Console.WriteLine($"client prof:   {report.Paths.GetValueOrDefault("client_profiles_path")}");
            Console.WriteLine($"config dir:    {report.Paths.GetValueOrDefault("config_dir")}");
            Console.WriteLine($"servers dir:   {report.Paths.GetValueOrDefault("presets_dir")}");
            Console.WriteLine($"projects root: {report.Paths.GetValueOrDefault("projects_root")}");
            Console.WriteLine($"enabled mods:  {report.Mods.Count}");
            foreach (var m in report.Mods)
                Console.WriteLine($"  - {m.Path}  ({m.Side})");
            Console.WriteLine("logs:");
            foreach (var kv in report.Logs)
                Console.WriteLine($"  {kv.Key,-7} {kv.Value ?? "(none)"}");
        });
        return statusCmd;
    }
}
