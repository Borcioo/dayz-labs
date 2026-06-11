namespace Dzl.Core.Build.Preflight;

/// <summary>
/// What the engine knows about the environment + which rule families run.
/// All checks default on except the expensive/binary ones noted below.
/// </summary>
public sealed record PreflightOptions
{
    /// <summary>Work-drive root references resolve against (normally <c>P:\</c>). Null/empty = skip
    /// work-drive candidates (pure project-relative resolution; used in tests).</summary>
    public string? WorkDriveRoot { get; init; } = @"P:\";

    /// <summary>CfgConvert.exe path for the config syntax gate. Null = gate skipped (a warning
    /// finding notes the skip so "0 errors" can't be mistaken for "configs parse").</summary>
    public string? CfgConvertExe { get; init; }

    /// <summary>Scratch dir for syntax-gate output. Defaults to the system temp.</summary>
    public string? TempDir { get; init; }

    public bool CheckConfig { get; init; } = true;          // CfgPatches / CfgMods / syntax gate
    public bool CheckReferences { get; init; } = true;      // quoted-path scan, rvmat textures
    public bool CheckFileSystem { get; init; } = true;      // case conflicts, paths, lowercase, freshness
    public bool CheckScripts { get; init; } = true;         // Enforce .c lint
    public bool CheckP3dStrings { get; init; } = true;      // binary string scan inside .p3d (warning-level)
    public bool CheckModCpp { get; init; } = true;          // mod.cpp presentation (when present)

    /// <summary>Extra exclude patterns (simple <c>*</c>/<c>?</c> globs, matched per path segment)
    /// on top of <see cref="DefaultExcludes"/>.</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>Dev-only files/folders that should never ship in a PBO. Directory names and file
    /// globs share one list; matching is case-insensitive per path segment.</summary>
    public static readonly string[] DefaultExcludes =
    {
        ".git", ".svn", ".vscode", ".idea", ".gui-sources", "workbench",
        ".gitignore", ".gitattributes", ".gitkeep", "readme.md", "*.gproj", "*.bak", "*.psd",
        "thumbs.db", "desktop.ini", ".dzl-build.json",
        // Prefix marker files never ship in the PBO (AddonBuilder consumes them).
        "$pboprefix$", "$prefix$", "$pboprefix$.txt", "$prefix$.txt",
    };
}
