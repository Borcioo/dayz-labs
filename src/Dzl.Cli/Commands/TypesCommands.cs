using System.CommandLine;
using Dzl.Core.App;

namespace Dzl.Cli.Commands;

/// <summary>types (ls / lint / set / rm / backups / restore) — Central Economy.</summary>
internal static class TypesCommands
{
    public static Command Types(CliContext c)
    {
        var typesCmd = new Command("types", "Edit the active server mission's Central Economy (types.xml).");

        var typesLsCmd = new Command("ls", "List types (name, nominal, min, lifetime, category, origin, file).");
        var typesLsFilter = new Option<string?>("--filter", () => null, "Substring filter on the type name.");
        var typesLsSource = new Option<string?>("--source", () => null, "Filter by origin: vanilla, mod, or custom.");
        var typesLsFile = new Option<string?>("--file", () => null, "Filter by source file basename substring.");
        typesLsCmd.AddOption(typesLsFilter);
        typesLsCmd.AddOption(typesLsSource);
        typesLsCmd.AddOption(typesLsFile);
        typesLsCmd.SetHandler(ctx =>
        {
            var filter = ctx.ParseResult.GetValueForOption(typesLsFilter);
            var source = ctx.ParseResult.GetValueForOption(typesLsSource);
            var fileFilter = ctx.ParseResult.GetValueForOption(typesLsFile);

            // Validate --source value before touching the config/disk.
            if (source is not null &&
                !string.Equals(source, "vanilla", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(source, "mod", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(source, "custom", StringComparison.OrdinalIgnoreCase))
            {
                CliOut.Fail(ctx, $"--source must be vanilla, mod, or custom (got '{source}')");
                return;
            }

            var (_, _, _, configPath) = c.Resolve(ctx);
            var svc = new TypesService(configPath);
            if (svc.TypesFile() is null) { CliOut.Fail(ctx, "no types.xml for the active server's mission"); return; }

            var rows = svc.Rows()
                .Where(r => filter is null || r.Entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Where(r => source is null || string.Equals(r.Origin.ToString(), source, StringComparison.OrdinalIgnoreCase))
                .Where(r => fileFilter is null || Path.GetFileName(r.Entry.SourceFile).Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Entry.Name);
            foreach (var row in rows)
            {
                var t = row.Entry;
                var originTag = $"[{row.Origin}]";
                var fileBase = string.IsNullOrEmpty(t.SourceFile) ? "" : Path.GetFileName(t.SourceFile);
                Console.WriteLine($"{t.Name,-32} nom={t.Nominal,-4} min={t.Min,-4} life={t.Lifetime,-7} {t.Category,-20} {originTag,-9} {fileBase}");
            }
        });
        typesCmd.AddCommand(typesLsCmd);

        var typesLintCmd = new Command("lint", "Run CE lint rules and report findings.");
        typesLintCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var svc = new TypesService(configPath);
            if (svc.TypesFile() is null) { CliOut.Fail(ctx, "no types.xml for the active server's mission"); return; }
            var findings = svc.Lint();
            if (findings.Count == 0) { Console.WriteLine("No CE lint findings."); return; }
            foreach (var f in findings)
            {
                var fileBase = string.IsNullOrEmpty(f.File) ? "" : Path.GetFileName(f.File);
                Console.WriteLine($"[{f.Severity}] {f.Code}: {f.Message}  ({f.EntryName} in {fileBase})");
            }
            ctx.ExitCode = 1;
        });
        typesCmd.AddCommand(typesLintCmd);

        var typesSetClass = new Argument<string>("Class", "Type/class name.");
        var typesSetNominal = new Option<int?>("--nominal", () => null, "Target spawn count.");
        var typesSetMin = new Option<int?>("--min", () => null, "Minimum before restock.");
        var typesSetLife = new Option<int?>("--lifetime", () => null, "Despawn seconds.");
        var typesSetRestock = new Option<int?>("--restock", () => null, "Restock seconds.");
        var typesSetCost = new Option<int?>("--cost", () => null, "Spawn priority cost.");
        var typesSetCat = new Option<string?>("--category", () => null, "Category name.");
        var typesSetFile = new Option<string?>("--file", () => null, "Target CE file basename (used only when creating a new type).");
        var typesSetCmd = new Command("set", "Set/insert a type (only the given fields change; backs up first).")
            { typesSetClass, typesSetNominal, typesSetMin, typesSetLife, typesSetRestock, typesSetCost, typesSetCat, typesSetFile };
        typesSetCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var r = new TypesService(configPath).Set(
                ctx.ParseResult.GetValueForArgument(typesSetClass),
                ctx.ParseResult.GetValueForOption(typesSetNominal),
                ctx.ParseResult.GetValueForOption(typesSetMin),
                ctx.ParseResult.GetValueForOption(typesSetLife),
                ctx.ParseResult.GetValueForOption(typesSetRestock),
                ctx.ParseResult.GetValueForOption(typesSetCost),
                ctx.ParseResult.GetValueForOption(typesSetCat),
                ctx.ParseResult.GetValueForOption(typesSetFile));
            CliOut.Result(ctx, r.Ok, r.Message);
        });
        typesCmd.AddCommand(typesSetCmd);

        var typesRmClass = new Argument<string>("Class", "Type/class name.");
        var typesRmCmd = new Command("rm", "Remove a type (backs up first).") { typesRmClass };
        typesRmCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var r = new TypesService(configPath).Remove(ctx.ParseResult.GetValueForArgument(typesRmClass));
            CliOut.Result(ctx, r.Ok, r.Message);
        });
        typesCmd.AddCommand(typesRmCmd);

        var typesBackupsCmd = new Command("backups", "List types.xml backups (newest first).");
        typesBackupsCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var b = new TypesService(configPath).Backups();
            CliOut.List(b, "(no backups)", x => $"{x.Stamp}  {x.Path}");
        });
        typesCmd.AddCommand(typesBackupsCmd);

        var typesRestoreArg = new Argument<string>("file", "Backup file path (see 'types backups').");
        var typesRestoreCmd = new Command("restore", "Restore a backup over the live types.xml (snapshots current first).") { typesRestoreArg };
        typesRestoreCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var r = new TypesService(configPath).Restore(ctx.ParseResult.GetValueForArgument(typesRestoreArg));
            CliOut.Result(ctx, r.Ok, r.Message);
        });
        typesCmd.AddCommand(typesRestoreCmd);
        return typesCmd;
    }
}
