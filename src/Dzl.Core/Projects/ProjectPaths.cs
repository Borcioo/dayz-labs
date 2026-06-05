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

    // ── Layout under ProjectsRoot ────────────────────────────────────────────
    //   mods\<Mod>\         source projects (+ .dzl\ metadata)   ←junction— P:\<Mod>
    //   build\@<Mod>\       build output (Addons\*.pbo)          ←junction— P:\Mods\@<Mod>
    //   servers\<name>\     server instances (+ .dzl\instance.json)
    //   bases\<name>\       server templates (+ .dzl\base.json)
    // Sources + builds live physically in ProjectsRoot; P: holds only junctions into them.

    /// <summary>The folder holding all mod source projects under the projects root.</summary>
    public static string ModsDir(string root) => Path.Combine(root, "mods");

    /// <summary>The source-project folder for a mod (<c>&lt;root&gt;\mods\&lt;Mod&gt;</c>).</summary>
    public static string ModDir(string root, string mod) => Path.Combine(root, "mods", mod);

    /// <summary>Per-mod dzl metadata folder (<c>&lt;root&gt;\mods\&lt;Mod&gt;\.dzl</c>).</summary>
    public static string ModMetaDir(string root, string mod) => Path.Combine(ModDir(root, mod), ".dzl");

    /// <summary>The folder holding all build output under the projects root.</summary>
    public static string BuildRoot(string root) => Path.Combine(root, "build");

    /// <summary>Build output folder for a mod (<c>&lt;root&gt;\build\@&lt;Mod&gt;</c>).</summary>
    public static string BuildDir(string root, string mod) => Path.Combine(BuildRoot(root), "@" + mod);

    /// <summary>The Addons folder a built PBO lands in (<c>&lt;root&gt;\build\@&lt;Mod&gt;\Addons</c>).</summary>
    public static string BuildAddonsDir(string root, string mod) => Path.Combine(BuildDir(root, mod), "Addons");

    /// <summary>The dzl ownership marker for a build (<c>&lt;root&gt;\build\@&lt;Mod&gt;\.dzl-build</c>).</summary>
    public static string BuildMarkerPath(string root, string mod) => Path.Combine(BuildDir(root, mod), ".dzl-build");

    /// <summary>Folder holding signing keys; the override if set, else <c>&lt;root&gt;\keys</c>.</summary>
    public static string KeysDir(string root, string? overrideDir) =>
        string.IsNullOrWhiteSpace(overrideDir) ? Path.Combine(root, "keys") : overrideDir!;

    /// <summary>Private signing key path (<c>&lt;keysDir&gt;\&lt;key&gt;.biprivatekey</c>) — never ship/commit this.</summary>
    public static string PrivateKey(string root, string? keysOverride, string keyName) =>
        Path.Combine(KeysDir(root, keysOverride), keyName + ".biprivatekey");

    /// <summary>Public key path (<c>&lt;keysDir&gt;\&lt;key&gt;.bikey</c>) — distributed in each mod's keys\.</summary>
    public static string PublicKey(string root, string? keysOverride, string keyName) =>
        Path.Combine(KeysDir(root, keysOverride), keyName + ".bikey");

    /// <summary>The built mod's <c>keys\</c> folder (<c>build\@&lt;Mod&gt;\keys</c>, sibling of <c>Addons\</c>) —
    /// where the public <c>.bikey</c> goes so the loadable/distributed <c>@&lt;Mod&gt;</c> carries it. It lives
    /// OUTSIDE the PBO (it must not be in the source, or AddonBuilder would pack it into the .pbo).</summary>
    public static string BuildKeysDir(string root, string mod) => Path.Combine(BuildDir(root, mod), "keys");

    /// <summary>steamcmd's install root for Workshop downloads (<c>&lt;root&gt;\workshop</c>). Items land under
    /// <c>&lt;root&gt;\workshop\steamapps\workshop\content\221100\&lt;id&gt;</c> — kept in the projects tree
    /// (passed to steamcmd via <c>+force_install_dir</c>) instead of buried next to steamcmd.exe.</summary>
    public static string WorkshopDir(string root) => Path.Combine(root, "workshop");

    /// <summary>The folder holding all server instances under the projects root.</summary>
    public static string ServersDir(string root) => Path.Combine(root, "servers");

    /// <summary>The folder for one server instance under the projects root.</summary>
    public static string ServerDir(string root, string instance) => Path.Combine(root, "servers", instance);

    /// <summary>The <b>P:</b> path for a mod — where AddonBuilder and the engine address the source once
    /// the work drive is mounted. P: is a subst/mount of the work-drive source folder, so this and
    /// <see cref="JunctionPath"/> point at the same reparse point when P: is up.</summary>
    public static string WorkDriveLink(string mod) => Path.Combine(@"P:\", mod);

    /// <summary>The physical junction path for a mod's <b>source</b> on the work-drive source folder (the
    /// always-live folder P: is mounted from, e.g. <c>D:\DayZWorkDrive</c>). Anchoring the junction here keeps
    /// its state stable across P: unmounts and lets us (re)create it offline; when P: is mounted,
    /// <see cref="WorkDriveLink"/> resolves to the same object. Falls back to <c>P:\</c> when the source is unknown.</summary>
    public static string JunctionPath(string? workDriveSource, string mod) =>
        Path.Combine(string.IsNullOrWhiteSpace(workDriveSource) ? @"P:\" : workDriveSource!, mod);

    /// <summary>The <b>P:</b> path the engine/toolchain addresses a built mod by (<c>P:\Mods\@&lt;Mod&gt;</c>).</summary>
    public static string BuildLink(string mod) => Path.Combine(@"P:\Mods", "@" + mod);

    /// <summary>The single junction for the whole build area: <c>&lt;source&gt;\Mods</c> → <see cref="BuildRoot"/>.
    /// One junction surfaces every <c>build\@&lt;Mod&gt;</c> at <c>P:\Mods\@&lt;Mod&gt;</c> (the build folders live
    /// under one parent, unlike sources which sit at the shared P:\ root and need per-mod links). Falls back to
    /// <c>P:\Mods</c> when the source folder is unknown.</summary>
    public static string BuildAreaJunction(string? workDriveSource) =>
        Path.Combine(string.IsNullOrWhiteSpace(workDriveSource) ? @"P:\" : workDriveSource!, "Mods");
}
