namespace Dzl.Core.Projects;

/// <summary>A mod source project under ProjectsRoot. <see cref="Linked"/> reflects the P:\ junction state.</summary>
public sealed record ModProject(string Name, string Path, bool Linked);

public static class ModProjects
{
    /// <summary>True if a directory looks like a mod source project (our PBOPREFIX marker, or a config.cpp).</summary>
    public static bool IsProject(string dir) =>
        File.Exists(System.IO.Path.Combine(dir, "$PBOPREFIX$")) ||
        File.Exists(System.IO.Path.Combine(dir, "config.cpp"));

    public static List<ModProject> Discover(string root)
    {
        var list = new List<ModProject>();
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (string.Equals(System.IO.Path.GetFileName(dir), "servers", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsProject(dir)) continue;
            var name = System.IO.Path.GetFileName(dir);
            var link = ProjectPaths.WorkDriveLink(name);
            list.Add(new ModProject(name, dir, Junction.IsLink(link)));
        }
        return list;
    }
}
