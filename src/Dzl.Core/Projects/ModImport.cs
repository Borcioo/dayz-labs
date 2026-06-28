namespace Dzl.Core.Projects;

public sealed record ImportResult(bool Ok, string ModDir, string Message);

/// <summary>Bring an external mod source folder into ProjectsRoot, then link P:\&lt;Name&gt;. Two modes:
/// <b>link</b> (default) junctions <c>mods\&lt;Name&gt;</c> at the source so it stays in place and edits affect the
/// original; <b>copy</b> duplicates the whole folder into <c>mods\&lt;Name&gt;</c> as an independent copy. Refuses
/// UNC sources and invalid names; copy refuses to clobber an existing project.</summary>
public static class ModImport
{
    public static ImportResult Import(string root, string source, string? name = null,
        string? workDriveSource = null, bool copy = false)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            return new ImportResult(false, "", $"source not found: {source}");
        if (source.StartsWith(@"\\", StringComparison.Ordinal))
            return new ImportResult(false, "", "UNC/network sources are not supported (junctions can't target them)");
        var modName = name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        if (!ProjectPaths.IsValidName(modName))
            return new ImportResult(false, "", $"invalid mod name: {modName}");

        var modDir = ProjectPaths.ModDir(root, modName);
        if (copy)
        {
            if (Directory.Exists(modDir) || File.Exists(modDir))
                return new ImportResult(false, modDir, $"already exists at {modDir} — pick another name");
            try { CopyTree(Path.GetFullPath(source), modDir); }
            catch (Exception ex) { return new ImportResult(false, modDir, $"copy failed: {ex.Message}"); }
        }
        else
        {
            var linkInProjects = Junction.Ensure(modDir, Path.GetFullPath(source));
            if (!linkInProjects.Ok) return new ImportResult(false, modDir, $"link into ProjectsRoot failed: {linkInProjects.Detail}");
        }

        var pLink = Junction.Ensure(ProjectPaths.JunctionPath(workDriveSource, modName), modDir);
        if (!pLink.Ok) return new ImportResult(false, modDir, $"P:\\ link failed: {pLink.Detail}");
        return new ImportResult(true, modDir, copy ? "copied" : "imported");
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, overwrite: true);
        }
    }
}
