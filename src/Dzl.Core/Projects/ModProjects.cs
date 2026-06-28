namespace Dzl.Core.Projects;

/// <summary>A mod source project under ProjectsRoot. <see cref="Linked"/> reflects the work-drive junction
/// state, checked on the always-live source folder (so it stays accurate even when P: is unmounted).
/// A <b>pack</b> (<see cref="IsPack"/>) is a folder that isn't itself a mod but whose subfolders are —
/// its <see cref="Children"/> are those inner mods; git/identity live at the pack level.</summary>
public sealed record ModProject(string Name, string Path, bool Linked)
{
    public bool IsPack { get; init; }

    /// <summary>True when <c>mods\&lt;Name&gt;</c> is itself a junction/symlink — i.e. the project was imported as a
    /// LINK to an external source (not created or copied here). Deleting such a project must remove only the link,
    /// never the external source it points at.</summary>
    public bool IsImportLink { get; init; }

    /// <summary>True when this is a leftover junction whose target is gone (the source folder was moved/deleted):
    /// the link still sits in <c>mods\</c> (and likely on P:\) but points nowhere. Surfaced so the user can clean
    /// the dead links up or re-point them, instead of the project silently vanishing.</summary>
    public bool IsBroken { get; init; }

    public IReadOnlyList<ModProject> Children { get; init; } = System.Array.Empty<ModProject>();
}

public static class ModProjects
{
    /// <summary>True if a directory looks like a mod source project (our PBOPREFIX marker, or a config.cpp).</summary>
    public static bool IsProject(string dir) =>
        File.Exists(System.IO.Path.Combine(dir, "$PBOPREFIX$")) ||
        File.Exists(System.IO.Path.Combine(dir, "config.cpp"));

    /// <summary>Enumerate mod source projects under <paramref name="root"/>. A direct child of <c>mods\</c>
    /// that is a project is listed as a standalone mod; a child that is NOT a project but contains project
    /// subfolders is listed as a <b>pack</b> with those inner mods as <see cref="ModProject.Children"/>
    /// (one level deep). The link state is checked on the work-drive <b>source</b> folder
    /// (<paramref name="workDriveSource"/>) so it stays accurate even when P: is unmounted; null falls back
    /// to the P:\ junction path.</summary>
    public static List<ModProject> Discover(string root, string? workDriveSource = null)
    {
        var list = new List<ModProject>();
        var modsDir = ProjectPaths.ModsDir(root);
        if (!Directory.Exists(modsDir)) return list;

        foreach (var dir in Sorted(Directory.GetDirectories(modsDir)))
        {
            try
            {
                // A folder with its own marker is a single mod, even if it also has project subfolders.
                if (IsProject(dir))
                {
                    list.Add(Make(dir, workDriveSource));
                    continue;
                }

                // Otherwise it's a pack if any immediate subfolder is a project.
                var children = Sorted(Directory.GetDirectories(dir))
                    .Where(IsProject)
                    .Select(child => Make(child, workDriveSource))
                    .ToList();
                if (children.Count > 0)
                    list.Add(Make(dir, workDriveSource) with { IsPack = true, Children = children });
            }
            catch
            {
                // Couldn't read into the entry. If it's a reparse-point left behind after its target was
                // moved/deleted, surface it as BROKEN so the user can clean the dead links up or re-point them —
                // don't let it silently vanish (the links still sit in mods\ and on P:). Any other IO error: skip.
                if (Junction.IsReparsePointEntry(dir))
                    list.Add(new ModProject(System.IO.Path.GetFileName(dir), dir, false)
                    { IsImportLink = true, IsBroken = true });
            }
        }
        return list;
    }

    private static ModProject Make(string dir, string? workDriveSource)
    {
        var name = System.IO.Path.GetFileName(dir);
        var link = ProjectPaths.JunctionPath(workDriveSource, name);
        return new ModProject(name, dir, Junction.IsLink(link)) { IsImportLink = Junction.IsLink(dir) };
    }

    private static IEnumerable<string> Sorted(IEnumerable<string> dirs) =>
        dirs.OrderBy(d => System.IO.Path.GetFileName(d), System.StringComparer.OrdinalIgnoreCase);
}
