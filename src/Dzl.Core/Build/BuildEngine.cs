using Dzl.Core.Tools;

namespace Dzl.Core.Build;

public sealed record EngineResult(bool Ok, string Pbo, string Output);

/// <summary>Direct build pipeline (no AddonBuilder, no FileBank): stage → Binarize (MLOD models only) →
/// CfgConvert (config.cpp→config.bin) → <see cref="PboWriter"/> (pack) → DSSignFile (sign). Already-binarized
/// (ODOL) p3d are excluded from Binarize and shipped unchanged, so they never crash the binarizer; a mod with no
/// MLOD models skips Binarize entirely. Binarize is given the project-drive context (<c>-binpath</c>/<c>-addon</c>,
/// cwd = <paramref name="binPath"/>) so it resolves vanilla + the mod's own materials and runs fast — the staging
/// itself can live off the project drive. Packing is done in-process, so no DayZ FileServer is involved. Never throws.</summary>
public static class BuildEngine
{
    /// <param name="binPath">The mounted project-drive root (<c>P:\</c>) passed to binarize as
    /// <c>-binpath</c>/<c>-addon</c> and used as its working directory, so it can resolve references.</param>
    /// <param name="addonFolders">Extra <c>-addon</c> scan folders for binarize (e.g. extracted vanilla packs).
    /// When null/empty, <paramref name="binPath"/> alone is scanned.</param>
    public static EngineResult Run(string toolsDir, string sourceDir, string prefix, string pboName,
        string workDir, string outAddonsDir, bool binarize, string? signPrivateKey, Action<string>? onLine,
        string binPath = @"P:\", IReadOnlyList<string>? addonFolders = null)
    {
        EngineResult Fail(string msg, string output = "") =>
            new(false, "", string.IsNullOrEmpty(output) ? msg : msg + "\n" + output);

        var binExe = ToolCatalog.Find(toolsDir, "binarize");
        if (binarize && (binExe is null || !binExe.Exists)) return Fail("binarize.exe not found — check the DayZ Tools path");

        try
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
        catch { /* leftover work dir — best effort */ }

        var final = Path.Combine(workDir, pboName);
        Directory.CreateDirectory(final);

        var odol = BuildAssets.BinarizedP3ds(sourceDir)
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Full copy of the source (configs, ODOL p3d as-is, $PBOPREFIX$, everything) → the pack folder. This
        //    already guarantees every model ships; Binarize output is overlaid on top, ODOL p3d are left untouched.
        CopyTree(sourceDir, final, exclude: null);

        if (binarize)
        {
            // 2. Binarize the MLOD models only. ODOL p3d are excluded (they'd crash the binarizer); a mod with no
            //    MLOD models at all (script/config-only, or ODOL-only) skips Binarize entirely.
            var hasModels = BuildAssets.P3ds(sourceDir).Select(Path.GetFullPath).Any(p => !odol.Contains(p));
            if (hasModels)
            {
                var stageBin = Path.Combine(workDir, "_bin_in");
                CopyTree(sourceDir, stageBin, exclude: odol);
                var binOut = Path.Combine(workDir, "_bin_out");
                var binTex = Path.Combine(workDir, "_bin_tex");
                Directory.CreateDirectory(binOut);
                Directory.CreateDirectory(binTex);
                var addons = addonFolders is { Count: > 0 } ? addonFolders : new[] { binPath };
                onLine?.Invoke($"binarize: {pboName}" + (odol.Count > 0 ? $"  ({odol.Count} ODOL p3d copied as-is)" : ""));
                var (bok, bout) = Binarize.Run(binExe!.ExePath, stageBin, binOut, binPath, addons,
                    binTex, Environment.ProcessorCount, onLine);
                if (!bok) return Fail($"binarize failed for {pboName}", bout);

                // 3. Overlay the binarized models onto the pack folder (replaces the MLOD source models).
                CopyTree(binOut, final, exclude: null);
            }
            else
            {
                onLine?.Invoke($"binarize: {pboName} — no MLOD models" +
                    (odol.Count > 0 ? $" ({odol.Count} ODOL p3d shipped as-is)" : "") + ", config only");
            }

            // 4. config.cpp → config.bin (root + nested); drop the .cpp once converted.
            var cfgExe = ToolCatalog.Find(toolsDir, "cfgconvert");
            if (cfgExe is not null && cfgExe.Exists)
                foreach (var cpp in Directory.EnumerateFiles(final, "config.cpp", SearchOption.AllDirectories))
                {
                    var bin = Path.Combine(Path.GetDirectoryName(cpp)!, "config.bin");
                    var (cok, cout) = CfgConvert.ToBin(cfgExe.ExePath, cpp, bin);
                    if (cok && File.Exists(bin)) { try { File.Delete(cpp); } catch { /* keep both */ } }
                    else onLine?.Invoke($"cfgconvert: kept {Path.GetFileName(cpp)} as .cpp ({cout.Trim()})");
                }
        }

        // 5. Pack the folder into <pboName>.pbo with the in-process writer (no FileBank / FileServer).
        Directory.CreateDirectory(outAddonsDir);
        var pbo = Path.Combine(outAddonsDir, pboName + ".pbo");
        onLine?.Invoke($"pack: {pboName}.pbo");
        var (pok, pout) = PboWriter.Pack(final, pbo, prefix, onLine);
        if (!pok || !File.Exists(pbo)) return Fail($"packing {pboName}.pbo failed", pout);

        // 6. Sign.
        if (!string.IsNullOrWhiteSpace(signPrivateKey))
        {
            var dsExe = ToolCatalog.Find(toolsDir, "dssignfile");
            if (dsExe is null || !dsExe.Exists) return Fail("DSSignFile.exe not found (signing requested)");
            onLine?.Invoke($"sign: {pboName}.pbo");
            var (sok, sout) = DsTools.Sign(dsExe.ExePath, signPrivateKey!, pbo);
            if (!sok) return Fail($"signing {pboName}.pbo failed", sout);
        }

        try { Directory.Delete(workDir, recursive: true); } catch { /* harmless leftover */ }
        return new EngineResult(true, pbo, "ok");
    }

    private static void CopyTree(string src, string dst, ISet<string>? exclude)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            if (exclude is not null && exclude.Contains(Path.GetFullPath(f))) continue;
            var target = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, overwrite: true);
        }
    }
}
