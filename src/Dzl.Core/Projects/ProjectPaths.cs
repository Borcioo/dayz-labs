using System.Text.RegularExpressions;
using Dzl.Core.Config;

namespace Dzl.Core.Projects;

/// <summary>Pure path math for mod projects + server instances under the configured ProjectsRoot.</summary>
public static partial class ProjectPaths
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex NameRx();

    /// <summary>A valid mod / instance name: starts with a letter, then letters/digits/underscores, max 64.</summary>
    public static bool IsValidName(string? name) => !string.IsNullOrEmpty(name) && NameRx().IsMatch(name);

    /// <summary>Pure: configured root if set, else <paramref name="userProfile"/>\DayZProjects.</summary>
    public static string Root(string? configured, string userProfile) =>
        string.IsNullOrWhiteSpace(configured) ? Path.Combine(userProfile, "DayZProjects") : configured;

    /// <summary>Resolve the root for a config using the real user profile.</summary>
    public static string Root(DzlConfig cfg) =>
        Root(cfg.ProjectsRoot, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The source-project folder for a mod under the projects root.</summary>
    public static string ModDir(string root, string mod) => Path.Combine(root, mod);

    /// <summary>The folder holding all server instances under the projects root.</summary>
    public static string ServersDir(string root) => Path.Combine(root, "servers");

    /// <summary>The folder for one server instance under the projects root.</summary>
    public static string ServerDir(string root, string instance) => Path.Combine(root, "servers", instance);

    /// <summary>The <b>P:</b> path for a mod — where AddonBuilder and the engine address the source once
    /// the work drive is mounted. P: is a subst/mount of the work-drive source folder, so this and
    /// <see cref="JunctionPath"/> point at the same reparse point when P: is up.</summary>
    public static string WorkDriveLink(string mod) => Path.Combine(@"P:\", mod);

    /// <summary>The physical junction path for a mod on the work-drive <b>source</b> folder (the always-live
    /// folder P: is mounted from, e.g. <c>D:\DayZWorkDrive</c> — read from DayZ Tools settings.ini). Managing
    /// the junction here keeps its state stable across P: unmounts and lets us (re)create it offline; when
    /// P: is mounted, <see cref="WorkDriveLink"/> resolves to the same object. Falls back to <c>P:\</c> when
    /// the source folder is unknown (then behaves like the old P:-anchored model).</summary>
    public static string JunctionPath(string? workDriveSource, string mod) =>
        Path.Combine(string.IsNullOrWhiteSpace(workDriveSource) ? @"P:\" : workDriveSource!, mod);
}
