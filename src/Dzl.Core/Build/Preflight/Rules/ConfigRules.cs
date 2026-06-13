using System.Text.RegularExpressions;
using Dzl.Core.Tools;

namespace Dzl.Core.Build.Preflight.Rules;

/// <summary>Config-layer checks: prefix file sanity, CfgPatches/requiredAddons, CfgMods ↔ scripts/
/// cross-check, and the CfgConvert syntax gate.</summary>
/// <remarks>Per official docs, CfgPatches is the only required part of a PBO and CfgMods is required
/// once the PBO ships scripts or inputs.</remarks>
public static class ConfigRules
{
    /// <summary>Classes whose vanilla definition lives in a known addon — inheriting from one
    /// implies a requiredAddons entry. Heuristic (info-level), not exhaustive.</summary>
    private static readonly (string BaseClass, string Addon)[] VanillaBaseHints =
    {
        ("Inventory_Base", "DZ_Data"),
        ("Clothing_Base", "DZ_Characters"),
        ("Clothing", "DZ_Characters"),
        ("Container_Base", "DZ_Gear_Containers"),
        ("Edible_Base", "DZ_Gear_Food"),
        ("Bottle_Base", "DZ_Gear_Drinks"),
        ("TentBase", "DZ_Gear_Camping"),
        ("CarScript", "DZ_Vehicles_Wheeled"),
        ("Weapon_Base", "DZ_Weapons_Firearms"),
        ("Rifle_Base", "DZ_Weapons_Firearms"),
        ("Magazine_Base", "DZ_Weapons_Magazines"),
    };

    /// <summary>Script-module config classes and the conventional folder each compiles.</summary>
    public static readonly (string Module, string Folder)[] ScriptModules =
    {
        ("engineScriptModule", @"scripts\1_Core"),
        ("gameLibScriptModule", @"scripts\2_GameLib"),
        ("gameScriptModule", @"scripts\3_Game"),
        ("worldScriptModule", @"scripts\4_World"),
        ("missionScriptModule", @"scripts\5_Mission"),
    };

    /// <summary>All config.cpp files in the mod (excluded folders skipped), root first.</summary>
    public static List<string> DiscoverConfigs(string modDir, string[] excludes)
    {
        var configs = new List<string>();
        foreach (var f in Directory.EnumerateFiles(modDir, "config.cpp", SearchOption.AllDirectories))
        {
            var rel = PathResolver.RelativeTo(f, modDir);
            if (rel is null || PathResolver.IsExcluded(rel, excludes)) continue;
            configs.Add(f);
        }
        configs.Sort(StringComparer.OrdinalIgnoreCase);
        return configs;
    }

    /// <summary>Include resolver shared by config reading: relative to the including file first,
    /// then the standard reference candidates.</summary>
    public static string? ResolveInclude(string include, string fromFile, string modDir, string prefix,
        string? workDriveRoot)
    {
        var local = Path.Combine(Path.GetDirectoryName(fromFile) ?? modDir,
            PathResolver.NormalizeRef(include).Replace('\\', Path.DirectorySeparatorChar));
        if (File.Exists(local)) return local;
        var (resolved, found) = PathResolver.Resolve(include, modDir, prefix, workDriveRoot);
        return found && File.Exists(resolved) ? resolved : null;
    }

    public static void CheckPrefixFile(string modDir, string modName, PreflightReport report)
    {
        var files = PathResolver.PrefixFiles(modDir);
        if (files.Count > 1)
            report.Warn("prefix-duplicate",
                $"Multiple PBO prefix files present: {string.Join(", ", files.Select(Path.GetFileName))} — only one will win.");
        if (files.Count == 0)
        {
            report.Info("prefix-missing",
                "No $PBOPREFIX$ file — the PBO name/folder name becomes the prefix. Fine for simple mods, but make it explicit once asset paths matter.");
            return;
        }

        var raw = PathResolver.ReadPrefix(modDir);
        var file = Path.GetFileName(files[0]);
        if (raw.Length == 0) { report.Warn("prefix-empty", $"{file} exists but is empty.", file); return; }
        if (raw.StartsWith("P:", StringComparison.OrdinalIgnoreCase))
            report.Warn("prefix-drive", $"PBO prefix starts with a drive letter: '{raw}' — prefixes are work-drive-relative.", file);

        var last = raw.Split('\\').Last();
        string Squash(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
        if (Squash(last).Length > 0 && !Squash(modName).Contains(Squash(last)) && !Squash(last).Contains(Squash(modName)))
            report.Info("prefix-mismatch",
                $"Prefix '{raw}' looks unrelated to the mod folder '{modName}' — double-check it's intentional.", file);
    }

    public static void CheckConfigs(string modDir, string modName, string prefix, PreflightOptions opts,
        PreflightReport report)
    {
        var excludes = PathResolver.EffectiveExcludes(opts);
        var configs = DiscoverConfigs(modDir, excludes);
        if (configs.Count == 0)
        {
            report.Error("config-missing", "No config.cpp anywhere in the mod — nothing will register in the engine.");
            return;
        }

        string ReadFull(string cfg) => CppText.ReadWithIncludes(cfg,
            (inc, from) => ResolveInclude(inc, from, modDir, prefix, opts.WorkDriveRoot));

        var contents = configs.ToDictionary(c => c, c => CppText.StripComments(ReadFull(c)));
        report.CheckedConfigs += configs.Count;

        CheckCfgPatches(modDir, configs, contents, report);
        CheckCfgMods(modDir, configs, contents, prefix, opts, report);
        SyntaxGate(modDir, configs, opts, report);
    }

    private static void CheckCfgPatches(string modDir, List<string> configs,
        Dictionary<string, string> contents, PreflightReport report)
    {
        var withPatches = configs
            .Where(c => CppText.FindClassBody(contents[c], "CfgPatches").Length > 0)
            .ToList();

        if (withPatches.Count == 0)
        {
            report.Error("cfgpatches-missing",
                "No CfgPatches class in any config.cpp — CfgPatches is the only required part of a PBO; without it the addon never registers.");
            return;
        }

        foreach (var cfg in withPatches)
        {
            var rel = PathResolver.RelativeTo(cfg, modDir) ?? cfg;
            var content = contents[cfg];
            var body = CppText.FindClassBody(content, "CfgPatches");
            var patchClasses = CppText.ClassBlocks(body).ToList();

            if (patchClasses.Count == 0)
            {
                report.Error("cfgpatches-empty", "CfgPatches exists but declares no addon patch class.", rel);
                continue;
            }

            // External-looking bases (defined nowhere in this config graph) imply dependencies.
            var defined = CppText.ClassBlocks(content).Select(b => b.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var externalBases = CppText.ClassBlocks(content)
                .Where(b => b.Base.Length > 0 && !defined.Contains(b.Base))
                .Select(b => b.Base)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var patch in patchClasses)
            {
                var patchLine = LineInFile(cfg, "class " + patch.Name);
                var required = CppText.ParseArrayValues(patch.Body, "requiredAddons");
                if (required is null)
                {
                    report.Warn("requiredaddons-missing",
                        $"CfgPatches class {patch.Name} has no requiredAddons[] — load order vs vanilla/other mods is undefined.", rel, patchLine);
                }
                else if (required.Count == 0 && externalBases.Count > 0)
                {
                    report.Warn("requiredaddons-empty",
                        $"requiredAddons[] is empty but the config inherits from external classes ({string.Join(", ", externalBases.Take(5))}).", rel, patchLine);
                }

                if (required is not null)
                {
                    var hints = VanillaBaseHints
                        .Where(h => externalBases.Any(b =>
                            b.Equals(h.BaseClass, StringComparison.OrdinalIgnoreCase)))
                        .Select(h => h.Addon)
                        .Distinct()
                        .Where(a => !required.Contains(a, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    if (hints.Count > 0)
                        report.Info("requiredaddons-hint",
                            $"Classes inherit from vanilla bases that live in: {string.Join(", ", hints)} — consider adding them to requiredAddons[] of {patch.Name}.", rel);
                }
            }
        }
    }

    /// <summary>1-based line of the first occurrence of <paramref name="needle"/> in the RAW file
    /// (0 when absent/unreadable). Inlined includes shift positions in the merged content, so
    /// line numbers for findings are always located in the original file text.</summary>
    private static int LineInFile(string path, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        try
        {
            var raw = File.ReadAllText(path);
            var idx = raw.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            return idx < 0 ? 0 : CppText.LineOf(raw, idx);
        }
        catch { return 0; }
    }

    private static void CheckCfgMods(string modDir, List<string> configs,
        Dictionary<string, string> contents, string prefix, PreflightOptions opts, PreflightReport report)
    {
        var scriptFolders = ScriptModules
            .Select(m => (m.Module, m.Folder, Path: Path.Combine(modDir, m.Folder.Replace('\\', Path.DirectorySeparatorChar))))
            .Where(m => Directory.Exists(m.Path))
            .ToList();

        // The config whose CfgMods body is richest wins (mods sometimes split configs).
        var best = configs
            .Select(c => (Config: c, Body: CppText.FindClassBody(contents[c], "CfgMods")))
            .Where(x => x.Body.Length > 0)
            .OrderByDescending(x => x.Body.Length)
            .FirstOrDefault();

        if (best.Config is null)
        {
            if (scriptFolders.Count > 0)
                report.Error("cfgmods-missing",
                    $"Script folders exist ({string.Join(", ", scriptFolders.Select(f => f.Folder))}) but no config declares CfgMods — those scripts will never compile. CfgMods with type=\"mod\" is required for script PBOs.");
            return;
        }

        var rel = PathResolver.RelativeTo(best.Config, modDir) ?? best.Config;
        var modClasses = CppText.ClassBlocks(best.Body).Where(b => b.StartIndex >= 0).ToList();
        var topLevelMod = modClasses.FirstOrDefault();
        if (topLevelMod is not null && scriptFolders.Count > 0 &&
            !Regex.IsMatch(topLevelMod.Body, "\\btype\\s*=\\s*\"mod\"", RegexOptions.IgnoreCase))
            report.Warn("cfgmods-type",
                $"CfgMods class {topLevelMod.Name} has no type=\"mod\" — required for script/input mods per official docs.",
                rel, LineInFile(best.Config, "class " + topLevelMod.Name));

        var referenced = new List<string>();
        foreach (var (module, folder) in ScriptModules)
        {
            var moduleBody = CppText.FindClassBody(best.Body, module);
            var folderPath = Path.Combine(modDir, folder.Replace('\\', Path.DirectorySeparatorChar));
            var folderExists = Directory.Exists(folderPath);

            if (moduleBody.Length == 0)
            {
                if (folderExists)
                    report.Warn("cfgmods-module-unregistered",
                        $"{folder} exists on disk but CfgMods has no {module} entry — its scripts silently never compile.",
                        rel, LineInFile(best.Config, "class CfgMods"));
                continue;
            }

            var files = CppText.ParseArrayValues(moduleBody, "files");
            if (files is null || files.Count == 0)
            {
                report.Warn("cfgmods-files-empty", $"{module} is declared but its files[] is missing/empty.",
                    rel, LineInFile(best.Config, module));
                continue;
            }

            foreach (var entry in files)
            {
                var (resolved, found) = PathResolver.Resolve(entry, modDir, prefix, opts.WorkDriveRoot);
                if (found) referenced.Add(Path.GetFullPath(resolved));
                else report.Warn("cfgmods-path-missing", $"{module} files[] path does not exist: {entry}",
                    rel, LineInFile(best.Config, entry));
            }
        }

        foreach (var (module, folder, path) in scriptFolders)
        {
            var full = Path.GetFullPath(path);
            var hit = referenced.Any(r =>
                string.Equals(r, full, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                r.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!hit && CppText.FindClassBody(best.Body, module).Length > 0)
                report.Warn("cfgmods-folder-unreferenced",
                    $"{folder} exists but no {module} files[] entry points at it.",
                    rel, LineInFile(best.Config, module));
        }
    }

    private static void SyntaxGate(string modDir, List<string> configs, PreflightOptions opts,
        PreflightReport report)
    {
        if (string.IsNullOrEmpty(opts.CfgConvertExe) || !File.Exists(opts.CfgConvertExe))
        {
            report.Warn("syntax-gate-skipped",
                "CfgConvert.exe not available — config syntax was NOT validated (set the DayZ Tools path to enable the gate).");
            return;
        }

        var tempRoot = Path.Combine(opts.TempDir ?? Path.GetTempPath(), "dzl-preflight");
        Directory.CreateDirectory(tempRoot);
        foreach (var cfg in configs)
        {
            var rel = PathResolver.RelativeTo(cfg, modDir) ?? cfg;
            var outBin = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".bin");
            var (ok, output) = CfgConvert.ToBin(opts.CfgConvertExe, cfg, outBin);
            try { if (File.Exists(outBin)) File.Delete(outBin); } catch { }
            if (!ok || output.Contains("error", StringComparison.OrdinalIgnoreCase))
                report.Error("config-syntax",
                    $"CfgConvert rejected {rel}: {Truncate(output, 400)}", rel);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
