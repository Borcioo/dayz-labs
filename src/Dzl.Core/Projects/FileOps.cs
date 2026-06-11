namespace Dzl.Core.Projects;

/// <summary>Filesystem helpers shared by project management flows.</summary>
public static class FileOps
{
    /// <summary>Recursively delete a directory even when it contains read-only files. Git marks
    /// its pack/object files read-only, so a plain <see cref="Directory.Delete(string, bool)"/>
    /// on any cloned project dies halfway ("Access to the path 'pack-….idx' is denied") and
    /// leaves a broken half-deleted tree. Clears attributes first, then deletes. No-op when the
    /// directory doesn't exist.</summary>
    public static void ForceDeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* deletion will surface the real error */ }
        }
        Directory.Delete(dir, recursive: true);
    }
}
