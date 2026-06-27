using Dzl.Core.Tools;

namespace Dzl.Core.Build;

public sealed record EngineResult(bool Ok, string Pbo, string Output);

/// <summary>Direct build pipeline (DayZ Tools binaries, no AddonBuilder): stage → Binarize (models) →
/// CfgConvert (config.cpp→config.bin) → FileBank (pack) → DSSignFile (sign). Already-binarized (ODOL) p3d
/// are excluded from Binarize and shipped unchanged, so they never crash the binarizer. Never throws.</summary>
public static class BuildEngine
{
    public static EngineResult Run(string toolsDir, string sourceDir, string prefix, string pboName,
        string workDir, string outAddonsDir, bool binarize, string? signPrivateKey, Action<string>? onLine)
    {
        EngineResult Fail(string msg, string output = "") =>
            new(false, "", string.IsNullOrEmpty(output) ? msg : msg + "\n" + output);

        var fbExe = ToolCatalog.Find(toolsDir, "filebank");
        if (fbExe is null || !fbExe.Exists) return Fail("FileBank.exe not found — check the DayZ Tools path");
        var binExe = ToolCatalog.Find(toolsDir, "binarize");
        if (binarize && (binExe is null || !binExe.Exists)) return Fail("binarize.exe not found — check the DayZ Tools path");

        try
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
        catch { /* leftover work dir — best effort */ }

        var final = Path.Combine(workDir, pboName);   // FileBank names the .pbo after this folder leaf
        Directory.CreateDirectory(final);

        var odol = BuildAssets.BinarizedP3ds(sourceDir)
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Full copy of the source (configs, ODOL p3d as-is, $PBOPREFIX$, everything) → the pack folder.
        CopyTree(sourceDir, final, exclude: null);

        if (binarize)
        {
            // 2. Binarize input = the source WITHOUT the ODOL p3d (so Binarize never chokes on them).
            var stageBin = Path.Combine(workDir, "_bin_in");
            CopyTree(sourceDir, stageBin, exclude: odol);
            var binOut = Path.Combine(workDir, "_bin_out");
            Directory.CreateDirectory(binOut);
            onLine?.Invoke($"binarize: {pboName}" + (odol.Count > 0 ? $"  ({odol.Count} ODOL p3d copied as-is)" : ""));
            var (bok, bout) = Binarize.Run(binExe!.ExePath, stageBin, binOut, onLine);
            if (!bok) return Fail($"binarize failed for {pboName}", bout);

            // 3. Overlay the binarized models onto the pack folder (replaces the MLOD source models).
            CopyTree(binOut, final, exclude: null);

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

        // 5. Pack the folder into <pboName>.pbo.
        Directory.CreateDirectory(outAddonsDir);
        onLine?.Invoke($"pack: {pboName}.pbo");
        var (pok, pout) = FileBank.Pack(fbExe.ExePath, final, outAddonsDir, prefix, onLine);
        var pbo = Path.Combine(outAddonsDir, pboName + ".pbo");
        if (!pok || !File.Exists(pbo)) return Fail($"FileBank produced no {pboName}.pbo", pout);

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
