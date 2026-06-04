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

/// <summary>
/// Symlink-first → junction-fallback link management for <c>P:\&lt;Mod&gt;</c>. SP0 ships the pure
/// <see cref="Decide"/> rule; the thin <c>mklink</c> runner that acts on it lands with SP1.
/// </summary>
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
}
