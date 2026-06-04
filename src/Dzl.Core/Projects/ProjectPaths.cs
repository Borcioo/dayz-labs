using System.Text.RegularExpressions;
using Dzl.Core.Config;

namespace Dzl.Core.Projects;

/// <summary>Pure path math for mod projects + server instances under the configured ProjectsRoot.</summary>
public static partial class ProjectPaths
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex NameRx();

    /// <summary>A valid mod / instance name: starts with a letter, then letters/digits/underscores, max 64.</summary>
    public static bool IsValidName(string name) => !string.IsNullOrEmpty(name) && NameRx().IsMatch(name);

    /// <summary>Pure: configured root if set, else <paramref name="userProfile"/>\DayZProjects.</summary>
    public static string Root(string? configured, string userProfile) =>
        string.IsNullOrWhiteSpace(configured) ? Path.Combine(userProfile, "DayZProjects") : configured;

    /// <summary>Resolve the root for a config using the real user profile.</summary>
    public static string Root(DzlConfig cfg) =>
        Root(cfg.ProjectsRoot, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string ModDir(string root, string mod) => Path.Combine(root, mod);
    public static string ServersDir(string root) => Path.Combine(root, "servers");
    public static string ServerDir(string root, string instance) => Path.Combine(root, "servers", instance);

    /// <summary>The P: junction path for a mod (where AddonBuilder/engine expect to see the source).</summary>
    public static string WorkDriveLink(string mod) => Path.Combine(@"P:\", mod);
}
