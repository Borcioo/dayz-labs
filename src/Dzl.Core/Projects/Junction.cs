using Dzl.Core.Tools;

namespace Dzl.Core.Projects;

/// <summary>What to do to make a link path point at the desired target.</summary>
public enum LinkAction
{
    AlreadyOk,        // a link already points at the target — nothing to do
    CreateNew,        // nothing there — create the link
    ReplaceStale,     // a link points elsewhere or dangles — remove + recreate
    ConflictRealDir,  // a real folder/file occupies the path — refuse (never clobber)
}

/// <summary>Result of a <see cref="Junction.Ensure"/> call.</summary>
public sealed record EnsureResult(bool Ok, LinkAction Action, string Detail);

/// <summary>Symlink-first → junction-fallback link management for <c>P:\&lt;Mod&gt;</c>. The pure
/// <see cref="Decide"/> rule chooses the action; a thin <c>mklink</c> runner acts on it.</summary>
public static class Junction
{
    /// <summary>Pure decision. <paramref name="currentTarget"/> is the link's resolved target, or null
    /// when unknown/dangling. Target equality reuses the tested <see cref="WorkDrive.SamePath"/>
    /// (case- and trailing-slash-insensitive).</summary>
    public static LinkAction Decide(bool exists, bool isLink, string? currentTarget, string desiredTarget)
    {
        if (!exists) return LinkAction.CreateNew;
        if (!isLink) return LinkAction.ConflictRealDir;
        return WorkDrive.SamePath(currentTarget, desiredTarget) ? LinkAction.AlreadyOk : LinkAction.ReplaceStale;
    }

    /// <summary>True if the path exists and is a reparse point (symlink/junction).</summary>
    public static bool IsLink(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            return di.Exists && di.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch { return false; }
    }

    /// <summary>Resolved target of a link, or null if not a link / unreadable / dangling.</summary>
    public static string? LinkTarget(string path)
    {
        try { return new DirectoryInfo(path).LinkTarget; }
        catch { return null; }
    }

    /// <summary>
    /// Make <paramref name="linkPath"/> a link to <paramref name="target"/>. Decides via
    /// <see cref="Decide"/>; creates with symlink-first (<c>mklink /D</c>) then junction
    /// fallback (<c>mklink /J</c>, no admin). Never clobbers a real directory. Never throws.
    /// </summary>
    public static EnsureResult Ensure(string linkPath, string target)
    {
        var exists = Directory.Exists(linkPath) || File.Exists(linkPath);
        var action = Decide(exists, IsLink(linkPath), LinkTarget(linkPath), target);
        try
        {
            switch (action)
            {
                case LinkAction.AlreadyOk:
                    return new EnsureResult(true, action, "link already correct");
                case LinkAction.ConflictRealDir:
                    return new EnsureResult(false, action, $"a real directory occupies {linkPath}");
                case LinkAction.ReplaceStale:
                    Remove(linkPath);
                    goto case LinkAction.CreateNew;
                case LinkAction.CreateNew:
                    Directory.CreateDirectory(target);
                    if (CreateLink(linkPath, target)) return new EnsureResult(true, action, "linked");
                    return new EnsureResult(false, action, "mklink failed");
                default:
                    return new EnsureResult(false, action, "unknown action");
            }
        }
        catch (Exception ex) { return new EnsureResult(false, action, ex.Message); }
    }

    /// <summary>Remove a link without descending into its target (delete the link itself).</summary>
    public static void Remove(string linkPath)
    {
        try { if (IsLink(linkPath)) Directory.Delete(linkPath); } catch { /* best-effort */ }
    }

    private static bool CreateLink(string linkPath, string target)
    {
        if (RunMklink("/D", linkPath, target) && IsLink(linkPath)) return true;
        return RunMklink("/J", linkPath, target) && IsLink(linkPath);
    }

    private static bool RunMklink(string flag, string linkPath, string target)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("mklink");
            psi.ArgumentList.Add(flag); psi.ArgumentList.Add(linkPath); psi.ArgumentList.Add(target);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }
}
