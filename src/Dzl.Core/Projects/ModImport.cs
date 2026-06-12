namespace Dzl.Core.Projects;

public sealed record ImportResult(bool Ok, string ModDir, string Message);

/// <summary>Link an external mod source folder into ProjectsRoot non-invasively (source stays in place),
/// then link P:\&lt;Name&gt;. Refuses UNC sources and invalid names.</summary>
public static class ModImport
{
    public static ImportResult Import(string root, string source, string? name = null, string? workDriveSource = null)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            return new ImportResult(false, "", $"source not found: {source}");
        if (source.StartsWith(@"\\", StringComparison.Ordinal))
            return new ImportResult(false, "", "UNC/network sources are not supported (junctions can't target them)");
        var modName = name ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(source));
        if (!ProjectPaths.IsValidName(modName))
            return new ImportResult(false, "", $"invalid mod name: {modName}");

        var modDir = ProjectPaths.ModDir(root, modName);
        var linkInProjects = Junction.Ensure(modDir, System.IO.Path.GetFullPath(source));
        if (!linkInProjects.Ok) return new ImportResult(false, modDir, $"link into ProjectsRoot failed: {linkInProjects.Detail}");
        var pLink = Junction.Ensure(ProjectPaths.JunctionPath(workDriveSource, modName), modDir);
        if (!pLink.Ok) return new ImportResult(false, modDir, $"P:\\ link failed: {pLink.Detail}");
        return new ImportResult(true, modDir, "imported");
    }
}
