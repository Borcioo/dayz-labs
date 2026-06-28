namespace Dzl.Core.Projects;

/// <summary>Filesystem helpers shared by project management flows.</summary>
public static class FileOps
{
    /// <summary>Recursively delete a directory even when it contains read-only files. Git marks
    /// its pack/object files read-only, so a plain <see cref="Directory.Delete(string, bool)"/>
    /// on any cloned project dies halfway ("Access to the path 'pack-….idx' is denied") and
    /// leaves a broken half-deleted tree. Clears attributes first, then deletes. No-op when the
    /// directory doesn't exist.</summary>
    /// <remarks>Reparse-point safe: a junction/symlink (the dir itself or any nested one) is removed as a LINK —
    /// we never recurse into its target. So deleting a mod folder that was imported as a link removes only the
    /// link and never touches (or wipes) the external source it points at.</remarks>
    public static void ForceDeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var di = new DirectoryInfo(dir);
        if (di.Attributes.HasFlag(FileAttributes.ReparsePoint)) { Directory.Delete(dir); return; }

        foreach (var sub in di.GetDirectories())
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                try { Directory.Delete(sub.FullName); } catch { /* leave a stubborn link for the final delete */ }
            else
                ForceDeleteDirectory(sub.FullName);
        }
        foreach (var f in di.GetFiles())
            try { f.Attributes = FileAttributes.Normal; } catch { /* deletion will surface the real error */ }
        Directory.Delete(dir, recursive: true);
    }
}
